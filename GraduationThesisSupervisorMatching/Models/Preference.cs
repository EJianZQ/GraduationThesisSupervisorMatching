namespace GraduationThesisSupervisorMatching.Models
{
    public class Preference
    {
        public long Id { get; set; }
        public long StudentId { get; set; }
        public Student Student { get; set; }
        public long TeacherId { get; set; }
        public Teacher Teacher { get; set; }
        public int PreferenceOrder { get; set; }
    }
}
