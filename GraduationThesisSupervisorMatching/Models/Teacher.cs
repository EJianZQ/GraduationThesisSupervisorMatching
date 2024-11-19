namespace GraduationThesisSupervisorMatching.Models
{
    public class Teacher
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public List<Student> RegularStudents { get; set; } = new List<Student>();
        public int CurrentCapacity => (RegularStudents?.Count ?? 0) + (BestStudent != null ? 1 : 0);
        public List<Preference> Preferences { get; set; } = new List<Preference>();
        public int MaxCapacity { get; set; }
        public long GradeId { get; set; }
        public Grade Grade { get; set; }
        public string Description { get; set; }
        public bool AcceptsTopStudent { get; set; } 
        public int UpperLevelCapacity { get; set; }
        public int MiddleLevelCapacity { get; set; }
        public int LowerLevelCapacity { get; set; }
        public long? BestStudentId { get; set; }
        public Student? BestStudent { get; set; }
    }
}