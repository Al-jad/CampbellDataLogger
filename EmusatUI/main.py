import gzip
import re
import threading
import tkinter as tk
from tkinter import ttk, messagebox
from datetime import datetime, timezone, timedelta

import customtkinter as ctk
import psycopg
import requests

# ── Config ────────────────────────────────────────────────────────────────────

DB_CONFIG = {
    "host": "108.181.197.94",
    "port": 5432,
    "dbname": "Cambell",
    "user": "postgres",
    "password": "YK7sPEBSFkvL6kee",
    "connect_timeout": 10,
}

EMUSAT_USER = "MOWR"
EMUSAT_PASS = "RO563hwhxeae"
SOURCE_ADDRESS = "service.eumetsat.int"
SYNC_INTERVAL_MIN = 60

DCPIDS = [
    "162382BC", "163F674C", "163FE158", "165364E2", "16AE269A", "165203FE",
    "16AFE17E", "16BA52AE", "16C117A0", "16C425FA", "16D9F76A", "16DFF7A4",
    "16E8F3E0", "16FDD552", "180B44E4", "181871EE", "1885F4BA", "1886A3C8",
    "18EF075E", "261D2602", "26246524", "26357336", "2648036C", "266CF73C",
    "2681E2A6", "2695A308", "28FA1228", "360955B2", "360D5088", "3624F7C2",
    "363E94E8", "363EF10E", "364E61C0", "3668C018", "36831052", "368DF7C8",
    "368E4348", "369FB6A8", "36A80162", "36B7E39C", "36BF82DC", "36F3B506",
    "362DB294", "3878F412",
]

MOWR77_ID = "1886A3C8"

HG_SEQ = bytes([0x20, 0x23, 0xB6, 0xB0, 0x20])
EOT_SEQ = bytes([0x20, 0xBB, 0x53, 0xC6])

# ── Binary parsing (ported from Worker.cs) ────────────────────────────────────

def process_segment(data: bytearray) -> str:
    for i in range(len(data)):
        if data[i] > 0x80:
            data[i] -= 0x80
        if data[i] >= 0x60:
            data[i] -= 0x40
        if data[i] == 0x2A:
            data[i] = 0x2E
        if data[i] == 0x24:
            data[i] = 0x20
        if data[i] == 0x00:
            data[i] = 0x20
        if 0x3C <= data[i] < 0x40:
            data[i] -= 0x04
    return data.decode("ascii", errors="replace")


def find_sequence(data: bytes, seq: bytes, offset: int = 0) -> int:
    idx = data.find(seq, offset)
    if idx == -1:
        raise ValueError("Sequence not found")
    return idx


def find_sequence_fuzzy(data: bytes, seq: bytes, offset: int = 0) -> int:
    for i in range(offset, len(data) - len(seq) + 1):
        matches = sum(1 for j in range(len(seq)) if data[i + j] == seq[j])
        if matches >= 3:
            return i
    raise ValueError("Sequence not found (fuzzy)")


def count_sequence(data: bytes, seq: bytes) -> int:
    count = 0
    i = 0
    while i <= len(data) - len(seq):
        if data[i:i + len(seq)] == seq:
            count += 1
            i += len(seq)
        else:
            i += 1
    return count


def try_parse_float(s: str) -> float:
    try:
        return float(s)
    except (ValueError, IndexError):
        return 0.0


def parse_battery_voltage(values: list[str], is_mowr77: bool) -> float:
    n = len(values)
    if n < 4:
        return 0.0

    if values[3] == "M" and n >= 5:
        v = try_parse_float(values[-4])
        if v:
            return v

    v = try_parse_float(values[-3])
    if v:
        return v

    if n > 6:
        v = try_parse_float(values[6])
        if v:
            return v

    if n >= 3 and len(values[-2]) >= 4:
        v = try_parse_float(values[-2][:4])
        if v:
            return v

    if n >= 5 and len(values[-4]) >= 4:
        v = try_parse_float(values[-4][:4])
        if v:
            return v

    fb_idx = 15 if is_mowr77 else 7
    if n > fb_idx:
        raw = values[fb_idx]
        candidate = raw[:4] if len(raw) >= 4 else raw
        v = try_parse_float(candidate)
        if v:
            return v

    return 0.0


_SALT_RE = re.compile(r"SAL[^#]*#\s*\d{2}\s*(\d+\.\d{2})", re.IGNORECASE)


def parse_salt(processed_segment: str) -> float | None:
    m = _SALT_RE.search(processed_segment)
    if m:
        try:
            return float(m.group(1))
        except ValueError:
            return None
    return None


def parse_entries(data: bytes, dcpid: str, last_ts: datetime, log_fn) -> list[dict]:
    results = []
    entry_count = count_sequence(data, EOT_SEQ)
    offset = 0
    entry_num = 0
    skipped = 0
    failed = 0

    while offset < len(data):
        try:
            eot_pos = find_sequence(data, EOT_SEQ, offset)
        except ValueError:
            break

        linewidth = eot_pos + 4 - offset
        try:
            row_bytes = data[offset:offset + linewidth]

            if len(row_bytes) < 0x48:
                log_fn("WARN", f"  [{dcpid}] entry {entry_num+1}/{entry_count}: too short ({len(row_bytes)}B), skip")
                failed += 1
                offset += linewidth
                entry_num += 1
                continue

            time_str = process_segment(bytearray(row_bytes[0x37:0x48])).strip()

            hg_abs = find_sequence_fuzzy(data, HG_SEQ, offset)
            hg_rel = hg_abs - offset

            data_start = max(0, hg_rel - 5)
            segment = bytearray(row_bytes[data_start:linewidth])
            processed_str = process_segment(segment)
            values = processed_str.split()

            try:
                ts = datetime.strptime(time_str, "%d/%m/%y %H:%M:%S")
                ts = ts.replace(tzinfo=timezone.utc)
            except ValueError:
                log_fn("WARN", f"  [{dcpid}] entry {entry_num+1}/{entry_count}: bad timestamp '{time_str}', skip")
                failed += 1
                offset += linewidth
                entry_num += 1
                continue

            if ts <= last_ts:
                skipped += 1
                offset += linewidth
                entry_num += 1
                continue

            is_mowr77 = dcpid == MOWR77_ID
            wl = values[7] if (is_mowr77 and len(values) > 7) else (values[3] if len(values) > 3 else "0")
            battery = parse_battery_voltage(values, is_mowr77)
            salt = parse_salt(processed_str)

            results.append({"wl": wl, "battery": battery, "salt": salt, "timestamp": ts})

        except Exception as ex:
            log_fn("WARN", f"  [{dcpid}] entry {entry_num+1}/{entry_count}: {ex}")
            failed += 1

        offset += linewidth
        entry_num += 1

    if failed:
        log_fn("WARN", f"  [{dcpid}] {failed}/{entry_count} entries failed to parse")
    if skipped:
        log_fn("INFO", f"  [{dcpid}] {skipped}/{entry_count} entries skipped (already saved)")

    return results


# ── EUMETSAT sync engine ─────────────────────────────────────────────────────

def _decompress_response(resp: requests.Response) -> bytes:
    raw = resp.content
    try:
        return gzip.decompress(raw)
    except (gzip.BadGzipFile, OSError):
        return raw


def _sync_dcpid(cur, conn, dcpid: str, session: requests.Session, log_fn) -> dict:
    stats = {"synced": 0, "new_rows": 0, "failed": 0, "skipped_stations": 0}
    url = (
        f"https://service.eumetsat.int/dcswebservice/dcpAdmin.do"
        f"?action=ACTION_DOWNLOAD&id={dcpid}"
        f"&user={EMUSAT_USER}&pass={EMUSAT_PASS}"
    )
    log_fn("INFO", f"[{dcpid}] Downloading...")

    resp = session.get(url, timeout=30)
    if resp.status_code != 200:
        log_fn("ERROR", f"[{dcpid}] HTTP {resp.status_code}")
        stats["failed"] += 1
        return stats

    raw = _decompress_response(resp)

    if len(raw) < 0x55:
        log_fn("WARN", f"[{dcpid}] Response too short ({len(raw)}B), skip")
        stats["skipped_stations"] += 1
        return stats

    station_name = raw[0x09:0x19].decode("ascii", errors="replace").strip()

    cur.execute('SELECT "Id" FROM "Stations" WHERE "ExternalId" = %s', (dcpid,))
    row = cur.fetchone()
    if row:
        station_id = row[0]
    else:
        cur.execute(
            'INSERT INTO "Stations" ("Name", "SourceAddress", "ExternalId", "City", "CreatedAt") '
            "VALUES (%s, %s, %s, %s, %s) RETURNING \"Id\"",
            (station_name, SOURCE_ADDRESS, dcpid,
             "\u0628\u063a\u062f\u0627\u062f", datetime.now(timezone.utc)),
        )
        station_id = cur.fetchone()[0]
        conn.commit()
        log_fn("OK", f"[{dcpid}] Created station '{station_name}'")

    cur.execute(
        'SELECT "TimeStamp" FROM "SensorData" '
        'WHERE "StationId" = %s ORDER BY "TimeStamp" DESC LIMIT 1',
        (station_id,),
    )
    last_row = cur.fetchone()
    if last_row and last_row[0]:
        last_ts = last_row[0]
        if last_ts.tzinfo is None:
            last_ts = last_ts.replace(tzinfo=timezone.utc)
    else:
        last_ts = datetime.now(timezone.utc) - timedelta(days=60)

    entries = parse_entries(raw, dcpid, last_ts, log_fn)

    if entries:
        with cur.copy(
            'COPY "SensorData" ("StationId", "WL", "BatteryVoltage", "Salt", "TimeStamp", "Record") '
            "FROM STDIN"
        ) as copy:
            for e in entries:
                copy.write_row((station_id, e["wl"], e["battery"], e["salt"], e["timestamp"], 0))
        conn.commit()
        log_fn("OK", f"[{dcpid}] Saved {len(entries)} new row(s)")
        stats["new_rows"] += len(entries)
    else:
        log_fn("INFO", f"[{dcpid}] Up to date")

    stats["synced"] += 1
    return stats


def sync_all_stations(log_fn, stop_event: threading.Event | None = None) -> dict:
    session = requests.Session()
    session.headers["Accept-Encoding"] = "gzip, deflate"
    totals = {"synced": 0, "new_rows": 0, "failed": 0, "skipped_stations": 0}
    all_dcpids = get_all_dcpids()
    log_fn("INFO", f"Syncing {len(all_dcpids)} DCPID(s)...")

    with psycopg.connect(**DB_CONFIG) as conn:
        with conn.cursor() as cur:
            for dcpid in all_dcpids:
                if stop_event and stop_event.is_set():
                    log_fn("WARN", "Sync cancelled")
                    break
                try:
                    s = _sync_dcpid(cur, conn, dcpid, session, log_fn)
                    for k in totals:
                        totals[k] += s[k]
                except Exception as ex:
                    log_fn("ERROR", f"[{dcpid}] {ex}")
                    totals["failed"] += 1
                    try:
                        conn.rollback()
                    except Exception:
                        pass
    return totals


def sync_single_station(dcpid: str, log_fn) -> dict:
    session = requests.Session()
    session.headers["Accept-Encoding"] = "gzip, deflate"
    with psycopg.connect(**DB_CONFIG) as conn:
        with conn.cursor() as cur:
            return _sync_dcpid(cur, conn, dcpid, session, log_fn)


# ── DB read queries (for the viewer) ─────────────────────────────────────────

QUERY_ALL = """
SELECT s."Id", s."Name", s."ExternalId", s."City",
       sd."WL", sd."BatteryVoltage", sd."Salt", sd."TimeStamp"
FROM "Stations" s
LEFT JOIN LATERAL (
    SELECT "WL", "BatteryVoltage", "Salt", "TimeStamp"
    FROM "SensorData"
    WHERE "StationId" = s."Id"
    ORDER BY "TimeStamp" DESC
    LIMIT 1
) sd ON true
WHERE s."SourceAddress" = %s
ORDER BY s."Name";
"""

QUERY_SINGLE = """
SELECT s."Id", s."Name", s."ExternalId", s."City",
       sd."WL", sd."BatteryVoltage", sd."Salt", sd."TimeStamp"
FROM "Stations" s
LEFT JOIN LATERAL (
    SELECT "WL", "BatteryVoltage", "Salt", "TimeStamp"
    FROM "SensorData"
    WHERE "StationId" = s."Id"
    ORDER BY "TimeStamp" DESC
    LIMIT 1
) sd ON true
WHERE s."SourceAddress" = %s AND s."Id" = %s;
"""

QUERY_DIAGNOSE = """
SELECT s."Name", s."ExternalId", COUNT(sd."Id") AS row_count,
       MAX(sd."TimeStamp") AS latest
FROM "Stations" s
LEFT JOIN "SensorData" sd ON sd."StationId" = s."Id"
WHERE s."SourceAddress" = %s
GROUP BY s."Id", s."Name", s."ExternalId"
ORDER BY row_count ASC, s."Name";
"""

COLUMNS = ("Name", "External ID", "City", "Water Level", "Battery (V)", "Salt", "Timestamp")
COL_KEYS = ("name", "external_id", "city", "wl", "battery", "salt", "timestamp")


def fetch_rows(station_id: int | None = None) -> tuple[list[dict], list[tuple]]:
    logs = []
    with psycopg.connect(**DB_CONFIG) as conn:
        logs.append(("INFO", f"Connected to {DB_CONFIG['host']}/{DB_CONFIG['dbname']}"))
        with conn.cursor() as cur:
            if station_id is None:
                logs.append(("INFO", "Fetching ALL stations latest data"))
                cur.execute(QUERY_ALL, (SOURCE_ADDRESS,))
            else:
                logs.append(("INFO", f"Fetching station id={station_id}"))
                cur.execute(QUERY_SINGLE, (SOURCE_ADDRESS, station_id))
            rows = cur.fetchall()
            logs.append(("INFO", f"Query returned {len(rows)} row(s)"))

    result = []
    no_data = []
    for row in rows:
        sid, name, ext_id, city, wl, battery, salt, ts = row
        ts_str = ""
        if ts is not None:
            if ts.tzinfo is None:
                ts = ts.replace(tzinfo=timezone.utc)
            ts_str = ts.strftime("%Y-%m-%d %H:%M UTC")
        else:
            no_data.append(ext_id or str(sid))
        result.append({
            "id": sid, "name": name or "", "external_id": ext_id or "",
            "city": city or "", "wl": wl if wl else "---",
            "battery": f"{battery:.3f}" if battery is not None else "---",
            "salt": f"{salt:.2f}" if salt is not None else "---",
            "timestamp": ts_str or "---",
        })

    if no_data:
        logs.append(("WARN", f"{len(no_data)} station(s) have NO data: {', '.join(no_data)}"))
    else:
        logs.append(("OK", f"All {len(result)} station(s) have data"))
    return result, logs


def fetch_station_list() -> tuple[list[dict], list[tuple]]:
    logs = []
    with psycopg.connect(**DB_CONFIG) as conn:
        logs.append(("INFO", f"Connected to {DB_CONFIG['host']}/{DB_CONFIG['dbname']}"))
        with conn.cursor() as cur:
            cur.execute(
                'SELECT "Id", "Name", "ExternalId" FROM "Stations" '
                'WHERE "SourceAddress" = %s ORDER BY "Name"',
                (SOURCE_ADDRESS,),
            )
            stations = [{"id": r[0], "name": r[1] or r[2], "external_id": r[2]} for r in cur.fetchall()]
    logs.append(("INFO", f"Loaded {len(stations)} station(s) from DB"))
    return stations, logs


def run_diagnose(log_fn):
    with psycopg.connect(**DB_CONFIG) as conn:
        log_fn("INFO", "Running diagnostic: row counts per station")
        with conn.cursor() as cur:
            cur.execute(QUERY_DIAGNOSE, (SOURCE_ADDRESS,))
            rows = cur.fetchall()

    log_fn("INFO", f"{'Station':<35} {'ExtID':<12} {'Rows':>8}  Latest Timestamp")
    log_fn("INFO", "-" * 78)
    for name, ext_id, count, latest in rows:
        ts = ""
        if latest:
            if latest.tzinfo is None:
                latest = latest.replace(tzinfo=timezone.utc)
            ts = latest.strftime("%Y-%m-%d %H:%M UTC")
        level = "WARN" if count == 0 else "INFO"
        log_fn(level, f"{(name or ''):<35} {(ext_id or ''):<12} {count:>8}  {ts or 'NO DATA'}")

    zeros = sum(1 for _, _, c, _ in rows if c == 0)
    log_fn("INFO", "-" * 78)
    if zeros:
        log_fn("WARN", f"{zeros} station(s) have 0 rows - worker may not have synced yet")
    else:
        log_fn("OK", "All stations have data in DB")


# ── DB station management ─────────────────────────────────────────────────────


def add_station_to_db(external_id: str, name: str, city: str = "",
                      lat: float | None = None, lng: float | None = None,
                      description: str = "", notes: str = "") -> int:
    with psycopg.connect(**DB_CONFIG) as conn:
        with conn.cursor() as cur:
            cur.execute('SELECT "Id" FROM "Stations" WHERE "ExternalId" = %s', (external_id,))
            if cur.fetchone():
                raise ValueError(f"Station with External ID '{external_id}' already exists")
            cur.execute(
                'INSERT INTO "Stations" '
                '("Name", "SourceAddress", "ExternalId", "City", "Lat", "Lng", '
                '"Description", "Notes", "CreatedAt") '
                "VALUES (%s, %s, %s, %s, %s, %s, %s, %s, %s) "
                'RETURNING "Id"',
                (name, SOURCE_ADDRESS, external_id, city or None,
                 lat, lng, description or None, notes or None,
                 datetime.now(timezone.utc)),
            )
            station_id = cur.fetchone()[0]
            conn.commit()
    return station_id


def update_station_in_db(station_id: int, *, name: str, external_id: str = "",
                         city: str = "", lat: float | None = None,
                         lng: float | None = None, description: str = "",
                         notes: str = ""):
    with psycopg.connect(**DB_CONFIG) as conn:
        with conn.cursor() as cur:
            cur.execute(
                'UPDATE "Stations" SET "Name" = %s, "ExternalId" = %s, "City" = %s, '
                '"Lat" = %s, "Lng" = %s, "Description" = %s, "Notes" = %s '
                'WHERE "Id" = %s',
                (name, external_id or None, city or None, lat, lng,
                 description or None, notes or None, station_id),
            )
            conn.commit()


def get_station_info(station_id: int) -> dict | None:
    with psycopg.connect(**DB_CONFIG) as conn:
        with conn.cursor() as cur:
            cur.execute(
                'SELECT "Id", "Name", "ExternalId", "City", "Lat", "Lng", '
                '"Description", "Notes" FROM "Stations" WHERE "Id" = %s',
                (station_id,),
            )
            row = cur.fetchone()
            if not row:
                return None
            return {
                "id": row[0], "name": row[1] or "", "external_id": row[2] or "",
                "city": row[3] or "", "lat": row[4], "lng": row[5],
                "description": row[6] or "", "notes": row[7] or "",
            }


def get_all_dcpids() -> list[str]:
    """Merge hardcoded DCPIDS with any extra ones added via the UI."""
    db_ids: set[str] = set()
    with psycopg.connect(**DB_CONFIG) as conn:
        with conn.cursor() as cur:
            cur.execute(
                'SELECT "ExternalId" FROM "Stations" '
                'WHERE "SourceAddress" = %s AND "ExternalId" IS NOT NULL',
                (SOURCE_ADDRESS,),
            )
            db_ids = {r[0] for r in cur.fetchall()}
    combined = list(DCPIDS)
    for eid in sorted(db_ids):
        if eid not in combined:
            combined.append(eid)
    return combined


# ── Station dialog ────────────────────────────────────────────────────────────


class StationDialog(ctk.CTkToplevel):
    def __init__(self, parent, mode="add", station_data=None, on_done=None):
        super().__init__(parent)
        self.transient(parent)
        self.grab_set()
        self._mode = mode
        self._data = station_data or {}
        self._on_done = on_done

        title = "Add New Station" if mode == "add" else f"Edit - {self._data.get('name', '')}"
        self.title(title)
        self.geometry("460x560")
        self.resizable(False, False)
        self._entries: dict[str, ctk.CTkEntry | ctk.CTkTextbox] = {}
        self._build()
        self.after(100, self.focus_force)

    def _build(self):
        main = ctk.CTkScrollableFrame(self)
        main.pack(fill="both", expand=True, padx=16, pady=12)

        d = self._data
        fields = [
            ("External ID (DCPID) *", "external_id", d.get("external_id", "")),
            ("Station Name *", "name", d.get("name", "")),
            ("City", "city", d.get("city", "")),
            ("Latitude", "lat", str(d["lat"]) if d.get("lat") is not None else ""),
            ("Longitude", "lng", str(d["lng"]) if d.get("lng") is not None else ""),
            ("Description", "description", d.get("description", "")),
        ]

        for label, key, val in fields:
            ctk.CTkLabel(main, text=label, anchor="w",
                         font=ctk.CTkFont(size=12, weight="bold")).pack(
                fill="x", padx=4, pady=(8, 1))
            e = ctk.CTkEntry(main, placeholder_text=label.replace(" *", ""))
            e.insert(0, val)
            e.pack(fill="x", padx=4, pady=(0, 2))
            self._entries[key] = e

        ctk.CTkLabel(main, text="Notes", anchor="w",
                     font=ctk.CTkFont(size=12, weight="bold")).pack(
            fill="x", padx=4, pady=(8, 1))
        notes_box = ctk.CTkTextbox(main, height=70)
        notes_box.insert("0.0", d.get("notes", ""))
        notes_box.pack(fill="x", padx=4, pady=(0, 2))
        self._entries["notes"] = notes_box

        btns = ctk.CTkFrame(main, fg_color="transparent")
        btns.pack(fill="x", pady=(16, 4))

        ctk.CTkButton(btns, text="Cancel", width=80,
                       fg_color="#6b7280", hover_color="#4b5563",
                       command=self.destroy).pack(side="left", padx=4)

        ctk.CTkButton(btns, text="Save", width=100,
                       command=lambda: self._submit(False)).pack(side="right", padx=4)

        if self._mode == "add":
            ctk.CTkButton(btns, text="Save & Sync", width=130,
                           fg_color="#16a34a", hover_color="#15803d",
                           command=lambda: self._submit(True)).pack(side="right", padx=4)

    def _val(self, key: str) -> str:
        w = self._entries[key]
        if isinstance(w, ctk.CTkTextbox):
            return w.get("0.0", "end").strip()
        return w.get().strip()

    def _submit(self, sync: bool):
        ext = self._val("external_id").upper()
        name = self._val("name")

        if not name:
            messagebox.showwarning("Required", "Station name is required.", parent=self)
            return
        if self._mode == "add" and not ext:
            messagebox.showwarning("Required", "External ID is required for new stations.", parent=self)
            return

        lat = lng = None
        for key, lbl in [("lat", "Latitude"), ("lng", "Longitude")]:
            raw = self._val(key)
            if raw:
                try:
                    v = float(raw)
                except ValueError:
                    messagebox.showwarning("Invalid", f"{lbl} must be a number.", parent=self)
                    return
                if key == "lat":
                    lat = v
                else:
                    lng = v

        city = self._val("city")
        desc = self._val("description")
        notes = self._val("notes")

        try:
            if self._mode == "add":
                sid = add_station_to_db(ext, name, city, lat, lng, desc, notes)
            else:
                sid = self._data["id"]
                update_station_in_db(sid, name=name, external_id=ext,
                                     city=city, lat=lat, lng=lng,
                                     description=desc, notes=notes)
        except Exception as e:
            messagebox.showerror("Database Error", str(e), parent=self)
            return

        self.destroy()
        if self._on_done:
            self._on_done({
                "id": sid, "external_id": ext, "name": name,
                "mode": self._mode, "sync": sync,
            })


# ── App ───────────────────────────────────────────────────────────────────────

class App(ctk.CTk):
    def __init__(self):
        super().__init__()

        ctk.set_appearance_mode("dark")
        ctk.set_default_color_theme("blue")

        self.title("EUMETSAT Station Monitor")
        self.geometry("1100x780")
        self.minsize(800, 560)

        self._station_map: dict[str, int] = {}
        self._station_extid_map: dict[str, str] = {}
        self._table_rows: dict[str, dict] = {}
        self._sort_col: str | None = None
        self._sort_asc: bool = True
        self._sync_stop = threading.Event()
        self._auto_sync_active = False
        self._auto_sync_timer: threading.Timer | None = None

        self._build_ui()
        self._load_stations_async()

    def _build_ui(self):
        # ── top bar ───────────────────────────────────────────────────────
        top = ctk.CTkFrame(self, height=56, corner_radius=0)
        top.pack(fill="x", side="top")
        top.pack_propagate(False)

        ctk.CTkLabel(
            top, text="EUMETSAT Station Monitor",
            font=ctk.CTkFont(size=16, weight="bold"),
        ).pack(side="left", padx=16, pady=12)

        ctrl = ctk.CTkFrame(top, fg_color="transparent")
        ctrl.pack(side="right", padx=12)

        self.btn_sync = ctk.CTkButton(
            ctrl, text="Sync All", width=90,
            fg_color="#16a34a", hover_color="#15803d",
            command=self._on_sync,
        )
        self.btn_sync.pack(side="left", padx=3, pady=10)

        self.btn_auto = ctk.CTkButton(
            ctrl, text="Auto: OFF", width=90,
            fg_color="#6b7280", hover_color="#4b5563",
            command=self._toggle_auto_sync,
        )
        self.btn_auto.pack(side="left", padx=3, pady=10)

        sep1 = ctk.CTkFrame(ctrl, width=2, height=28, fg_color="#45475a")
        sep1.pack(side="left", padx=6)

        ctk.CTkLabel(ctrl, text="Station:").pack(side="left", padx=(4, 2))

        self.station_var = tk.StringVar(value="-- loading --")
        self.station_combo = ctk.CTkComboBox(
            ctrl, variable=self.station_var,
            values=["-- loading --"],
            width=250,
        )
        self.station_combo.pack(side="left", padx=3)

        self.btn_fetch_one = ctk.CTkButton(
            ctrl, text="View", width=60,
            command=self._on_fetch_one,
        )
        self.btn_fetch_one.pack(side="left", padx=3)

        self.btn_sync_one = ctk.CTkButton(
            ctrl, text="Sync One", width=80,
            fg_color="#0ea5e9", hover_color="#0284c7",
            command=self._on_sync_one,
        )
        self.btn_sync_one.pack(side="left", padx=3)

        sep2 = ctk.CTkFrame(ctrl, width=2, height=28, fg_color="#45475a")
        sep2.pack(side="left", padx=6)

        self.btn_refresh = ctk.CTkButton(
            ctrl, text="Refresh", width=70,
            command=self._on_refresh_all,
        )
        self.btn_refresh.pack(side="left", padx=3, pady=10)

        self.btn_diagnose = ctk.CTkButton(
            ctrl, text="Diagnose", width=80,
            fg_color="#7c3aed", hover_color="#6d28d9",
            command=self._on_diagnose,
        )
        self.btn_diagnose.pack(side="left", padx=3)

        sep3 = ctk.CTkFrame(ctrl, width=2, height=28, fg_color="#45475a")
        sep3.pack(side="left", padx=6)

        self.btn_add = ctk.CTkButton(
            ctrl, text="+ Add", width=70,
            fg_color="#f59e0b", hover_color="#d97706",
            command=self._on_add_station,
        )
        self.btn_add.pack(side="left", padx=3)

        self.btn_edit = ctk.CTkButton(
            ctrl, text="Edit", width=60,
            fg_color="#8b5cf6", hover_color="#7c3aed",
            command=self._on_edit_selected,
        )
        self.btn_edit.pack(side="left", padx=3)

        # ── status bar ────────────────────────────────────────────────────
        status_bar = ctk.CTkFrame(self, height=28, corner_radius=0, fg_color="#11111b")
        status_bar.pack(fill="x", side="bottom")
        status_bar.pack_propagate(False)

        self.status_var = tk.StringVar(value="Connecting...")
        ctk.CTkLabel(
            status_bar, textvariable=self.status_var,
            font=ctk.CTkFont(size=11), text_color="#6c7086",
        ).pack(side="left", padx=12)

        # ── paned: table + log ────────────────────────────────────────────
        pane = tk.PanedWindow(
            self, orient="vertical",
            bg="#11111b", sashwidth=6, sashrelief="flat", sashpad=2,
        )
        pane.pack(fill="both", expand=True)

        # table
        table_outer = tk.Frame(pane, bg="#1e1e2e")
        pane.add(table_outer, stretch="always", minsize=120, height=400)

        style = ttk.Style(self)
        style.theme_use("clam")
        style.configure("T.Treeview", background="#1e1e2e", foreground="#cdd6f4",
                         fieldbackground="#1e1e2e", rowheight=28,
                         font=("Segoe UI", 10), borderwidth=0)
        style.configure("T.Treeview.Heading", background="#313244",
                         foreground="#89b4fa", font=("Segoe UI", 10, "bold"), relief="flat")
        style.map("T.Treeview", background=[("selected", "#45475a")],
                   foreground=[("selected", "#cdd6f4")])
        style.map("T.Treeview.Heading", background=[("active", "#45475a")])

        self.tree = ttk.Treeview(
            table_outer, columns=COL_KEYS, show="headings",
            style="T.Treeview", selectmode="browse",
        )
        col_widths = [200, 120, 120, 100, 100, 70, 175]
        for key, label, w in zip(COL_KEYS, COLUMNS, col_widths):
            self.tree.heading(key, text=label, command=lambda c=key: self._sort_by(c))
            self.tree.column(key, width=w, anchor="w", minwidth=60)

        vsb = ttk.Scrollbar(table_outer, orient="vertical", command=self.tree.yview)
        self.tree.configure(yscrollcommand=vsb.set)
        self.tree.pack(side="left", fill="both", expand=True)
        vsb.pack(side="right", fill="y")

        self.tree.tag_configure("odd", background="#181825")
        self.tree.tag_configure("even", background="#1e1e2e")
        self.tree.tag_configure("nodata", background="#2a1a1a", foreground="#f38ba8")

        self.tree.bind("<Double-1>", self._on_row_double_click)

        # log
        log_outer = tk.Frame(pane, bg="#11111b")
        pane.add(log_outer, stretch="never", minsize=80, height=240)

        log_header = tk.Frame(log_outer, bg="#181825", height=30)
        log_header.pack(fill="x")
        log_header.pack_propagate(False)

        tk.Label(log_header, text="  Terminal / Logs", bg="#181825", fg="#89b4fa",
                 font=("Segoe UI", 9, "bold")).pack(side="left", padx=4)
        tk.Button(log_header, text="Clear", bg="#313244", fg="#cdd6f4",
                  relief="flat", activebackground="#45475a", activeforeground="#cdd6f4",
                  bd=0, padx=8, font=("Segoe UI", 8),
                  command=self._clear_log).pack(side="right", padx=6, pady=4)

        self.log_box = tk.Text(
            log_outer, bg="#0d0d1a", fg="#cdd6f4", font=("Consolas", 9),
            state="disabled", wrap="none", borderwidth=0, relief="flat",
            insertbackground="#cdd6f4",
        )
        log_vsb = ttk.Scrollbar(log_outer, orient="vertical", command=self.log_box.yview)
        log_hsb = ttk.Scrollbar(log_outer, orient="horizontal", command=self.log_box.xview)
        self.log_box.configure(yscrollcommand=log_vsb.set, xscrollcommand=log_hsb.set)
        log_hsb.pack(side="bottom", fill="x")
        log_vsb.pack(side="right", fill="y")
        self.log_box.pack(side="left", fill="both", expand=True)

        self.log_box.tag_configure("INFO",  foreground="#89dceb")
        self.log_box.tag_configure("OK",    foreground="#a6e3a1")
        self.log_box.tag_configure("WARN",  foreground="#f9e2af")
        self.log_box.tag_configure("ERROR", foreground="#f38ba8")
        self.log_box.tag_configure("ts",    foreground="#585b70")

    # ── log helpers ───────────────────────────────────────────────────────

    def log(self, level: str, msg: str):
        ts = datetime.now().strftime("%H:%M:%S")
        self.log_box.configure(state="normal")
        self.log_box.insert("end", f"[{ts}] ", "ts")
        self.log_box.insert("end", f"[{level}] ", level)
        self.log_box.insert("end", msg + "\n")
        self.log_box.configure(state="disabled")
        self.log_box.see("end")

    def _log_threadsafe(self, level: str, msg: str):
        self.after(0, lambda: self.log(level, msg))

    def _clear_log(self):
        self.log_box.configure(state="normal")
        self.log_box.delete("1.0", "end")
        self.log_box.configure(state="disabled")

    # ── busy state ────────────────────────────────────────────────────────

    def _set_busy(self, busy: bool):
        state = "disabled" if busy else "normal"
        self.btn_refresh.configure(state=state)
        self.btn_fetch_one.configure(state=state)
        self.btn_sync_one.configure(state=state)
        self.btn_diagnose.configure(state=state)
        self.btn_sync.configure(state=state)
        if busy:
            self.status_var.set("Loading...")

    # ── station dropdown loader ───────────────────────────────────────────

    def _load_stations_async(self):
        self.log("INFO", "Starting up -- loading station list...")
        def worker():
            try:
                stations, logs = fetch_station_list()
                self.after(0, lambda: [self.log(l, m) for l, m in logs])
                self.after(0, lambda: self._populate_dropdown(stations))
                self.after(0, self._on_refresh_all)
            except Exception as exc:
                self.after(0, lambda: self._on_error(exc))
        threading.Thread(target=worker, daemon=True).start()

    def _populate_dropdown(self, stations: list[dict]):
        labels = [f"{s['name']}  ({s['external_id']})" for s in stations]
        self._station_map = {lbl: s["id"] for lbl, s in zip(labels, stations)}
        self._station_extid_map = {lbl: s["external_id"] for lbl, s in zip(labels, stations)}
        self.station_combo.configure(values=labels)
        if labels:
            self.station_var.set(labels[0])

    def _on_error(self, exc: Exception):
        self._set_busy(False)
        self.log("ERROR", str(exc))
        self.status_var.set(f"Error: {exc}")
        messagebox.showerror("Error", str(exc))

    # ── Sync (replaces the C# worker) ────────────────────────────────────

    def _on_sync(self):
        self._set_busy(True)
        self._sync_stop.clear()
        self.log("INFO", "=== Starting EUMETSAT sync ===")

        def worker():
            try:
                stats = sync_all_stations(self._log_threadsafe, self._sync_stop)
                self._log_threadsafe("OK",
                    f"=== Sync complete: {stats['synced']} stations, "
                    f"{stats['new_rows']} new rows, {stats['failed']} failed ===")
                self.after(0, lambda: self.status_var.set(
                    f"Sync done -- {stats['new_rows']} new rows"))
                self.after(0, self._on_refresh_all)
            except Exception as ex:
                self.after(0, lambda: self._on_error(ex))
            finally:
                self.after(0, lambda: self._set_busy(False))

        threading.Thread(target=worker, daemon=True).start()

    def _on_sync_one(self):
        label = self.station_var.get()
        ext_id = self._station_extid_map.get(label)
        if not ext_id:
            messagebox.showwarning("No station", "Select a station first.")
            return
        self._set_busy(True)
        self.log("INFO", f"=== Syncing single station: {label} ===")

        def worker():
            try:
                stats = sync_single_station(ext_id, self._log_threadsafe)
                self._log_threadsafe("OK",
                    f"=== Single sync done: {stats['new_rows']} new rows ===")
                self.after(0, lambda: self.status_var.set(
                    f"Sync done -- {stats['new_rows']} new rows"))
                self.after(0, self._on_refresh_all)
            except Exception as ex:
                self._log_threadsafe("ERROR", f"[{ext_id}] {ex}")
                self.after(0, lambda: self._on_error(ex))
            finally:
                self.after(0, lambda: self._set_busy(False))

        threading.Thread(target=worker, daemon=True).start()

    # ── Auto-sync ─────────────────────────────────────────────────────────

    def _toggle_auto_sync(self):
        if self._auto_sync_active:
            self._auto_sync_active = False
            if self._auto_sync_timer:
                self._auto_sync_timer.cancel()
                self._auto_sync_timer = None
            self.btn_auto.configure(text="Auto: OFF", fg_color="#6b7280",
                                     hover_color="#4b5563")
            self.log("INFO", "Auto-sync disabled")
        else:
            self._auto_sync_active = True
            self.btn_auto.configure(text=f"Auto: {SYNC_INTERVAL_MIN}m",
                                     fg_color="#16a34a", hover_color="#15803d")
            self.log("OK", f"Auto-sync enabled (every {SYNC_INTERVAL_MIN} min)")
            self._schedule_auto_sync()

    def _schedule_auto_sync(self):
        if not self._auto_sync_active:
            return

        def fire():
            if self._auto_sync_active:
                self.after(0, self._auto_sync_tick)

        self._auto_sync_timer = threading.Timer(SYNC_INTERVAL_MIN * 60, fire)
        self._auto_sync_timer.daemon = True
        self._auto_sync_timer.start()

    def _auto_sync_tick(self):
        if not self._auto_sync_active:
            return
        self.log("INFO", f"Auto-sync triggered (every {SYNC_INTERVAL_MIN} min)")
        self._on_sync()
        self._schedule_auto_sync()

    # ── View buttons ──────────────────────────────────────────────────────

    def _on_refresh_all(self):
        self._set_busy(True)
        self.log("INFO", "Refreshing all stations from DB...")

        def worker():
            try:
                rows, logs = fetch_rows()
                self.after(0, lambda: [self.log(l, m) for l, m in logs])
                self.after(0, lambda: self._populate_table(rows))
                n = len(rows)
                nd = sum(1 for r in rows if r["timestamp"] == "---")
                msg = f"Ready -- {n} station(s)"
                if nd:
                    msg += f" | {nd} with no data"
                self.after(0, lambda: self.status_var.set(msg))
            except Exception as ex:
                self.after(0, lambda: self._on_error(ex))
            finally:
                self.after(0, lambda: self._set_busy(False))

        threading.Thread(target=worker, daemon=True).start()

    def _on_fetch_one(self):
        label = self.station_var.get()
        station_id = self._station_map.get(label)
        if station_id is None:
            messagebox.showwarning("No station", "Select a station first.")
            return
        self._set_busy(True)
        self.log("INFO", f"Fetching: {label}")

        def worker():
            try:
                rows, logs = fetch_rows(station_id)
                self.after(0, lambda: [self.log(l, m) for l, m in logs])
                self.after(0, lambda: self._populate_table(rows))
                self.after(0, lambda: self.status_var.set(f"Showing 1 station"))
            except Exception as ex:
                self.after(0, lambda: self._on_error(ex))
            finally:
                self.after(0, lambda: self._set_busy(False))

        threading.Thread(target=worker, daemon=True).start()

    def _on_diagnose(self):
        self._set_busy(True)
        self.log("INFO", "Running DB diagnostic...")

        def worker():
            try:
                run_diagnose(self._log_threadsafe)
                self.after(0, lambda: self.status_var.set("Diagnostic complete"))
            except Exception as ex:
                self.after(0, lambda: self._on_error(ex))
            finally:
                self.after(0, lambda: self._set_busy(False))

        threading.Thread(target=worker, daemon=True).start()

    # ── station management ────────────────────────────────────────────────

    def _on_add_station(self):
        StationDialog(self, mode="add", on_done=self._station_dialog_done)

    def _on_edit_selected(self):
        sel = self.tree.selection()
        if not sel:
            messagebox.showinfo("Select", "Select a station row first (or double-click one).")
            return
        self._open_edit_dialog(sel[0])

    def _on_row_double_click(self, event):
        iid = self.tree.identify_row(event.y)
        if iid:
            self._open_edit_dialog(iid)

    def _open_edit_dialog(self, iid: str):
        row_data = self._table_rows.get(iid)
        if not row_data:
            return
        station_id = row_data["id"]
        self.log("INFO", "Loading station details...")

        def worker():
            try:
                info = get_station_info(station_id)
                if info:
                    self.after(0, lambda: StationDialog(
                        self, mode="edit", station_data=info,
                        on_done=self._station_dialog_done))
                else:
                    self.after(0, lambda: messagebox.showerror(
                        "Error", "Station not found in DB"))
            except Exception as ex:
                self.after(0, lambda: messagebox.showerror("Error", str(ex)))

        threading.Thread(target=worker, daemon=True).start()

    def _station_dialog_done(self, result: dict):
        action = "added" if result["mode"] == "add" else "updated"
        self.log("OK", f"Station '{result['name']}' {action}")
        self._load_stations_async()

        if result.get("sync") and result.get("external_id"):
            ext = result["external_id"]
            if ext not in DCPIDS:
                DCPIDS.append(ext)
            self._set_busy(True)
            self.log("INFO", f"=== Syncing new station {ext} ===")

            def worker():
                try:
                    stats = sync_single_station(ext, self._log_threadsafe)
                    self._log_threadsafe("OK",
                        f"=== Sync done: {stats['new_rows']} new rows ===")
                    self.after(0, self._on_refresh_all)
                except Exception as ex:
                    self._log_threadsafe("ERROR", f"[{ext}] {ex}")
                finally:
                    self.after(0, lambda: self._set_busy(False))

            threading.Thread(target=worker, daemon=True).start()

    # ── table ─────────────────────────────────────────────────────────────

    def _populate_table(self, rows: list[dict]):
        self._table_rows = {str(row["id"]): row for row in rows}
        self.tree.delete(*self.tree.get_children())
        for i, row in enumerate(rows):
            iid = str(row["id"])
            has_data = row["timestamp"] != "---"
            tag = "nodata" if not has_data else ("even" if i % 2 == 0 else "odd")
            self.tree.insert("", "end", iid=iid, values=(
                row["name"], row["external_id"], row["city"],
                row["wl"], row["battery"], row["salt"], row["timestamp"],
            ), tags=(tag,))

    def _sort_by(self, col: str):
        if self._sort_col == col:
            self._sort_asc = not self._sort_asc
        else:
            self._sort_col = col
            self._sort_asc = True

        items = [(self.tree.set(iid, col), iid) for iid in self.tree.get_children("")]
        items.sort(key=lambda x: x[0].lower(), reverse=not self._sort_asc)

        for rank, (_, iid) in enumerate(items):
            self.tree.move(iid, "", rank)
            cur_tags = self.tree.item(iid, "tags")
            if "nodata" not in cur_tags:
                self.tree.item(iid, tags=("even" if rank % 2 == 0 else "odd",))

        arrow = " \u25b2" if self._sort_asc else " \u25bc"
        for key, label in zip(COL_KEYS, COLUMNS):
            self.tree.heading(key, text=label + (arrow if key == col else ""))

    def destroy(self):
        self._sync_stop.set()
        self._auto_sync_active = False
        if self._auto_sync_timer:
            self._auto_sync_timer.cancel()
        super().destroy()


if __name__ == "__main__":
    app = App()
    app.mainloop()
