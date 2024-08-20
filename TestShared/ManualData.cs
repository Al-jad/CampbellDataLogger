using TestWorkerService;

namespace TestShared
{
    public class ManualData
    {
        public long Id { get; set; }
        public DateTime? TimeStamp { get; set; }
        public long StationId { get; set; }
        public Station Station { get; set; } = null!;

        public double PH { get; set; }
        public double Temp { get; set; }
        public double DO2 { get; set; }
        public double BOD5 { get; set; }
        public double PO4 { get; set; }
        public double NO3 { get; set; }
        public double Ca { get; set; }
        public double Mg { get; set; }
        public double TH { get; set; }
        public double K { get; set; }
        public double Na { get; set; }
        public double SO4 { get; set; }
        public double CL { get; set; }
        public double TDS { get; set; }
        public double EC { get; set; }
        public double Alk { get; set; }
        public double Acid { get; set; }
        public double OnG { get; set; }

        public double WQI { get; set; }
    }

}