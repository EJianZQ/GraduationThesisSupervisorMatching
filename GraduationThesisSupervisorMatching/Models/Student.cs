namespace GraduationThesisSupervisorMatching.Models
{
    public class Student
    {
        public long Id { get; set; }
        public string StudentID { get; set; }
        public string Name { get; set; }
        public string PasswordHash { get; set; }
        public decimal GPA { get; set; }
        public List<Preference> Preferences { get; set; } = new List<Preference>();
        public long? AssignedTeacherId { get; set; }
        public Teacher? AssignedTeacher { get; set; }
        public long GradeId { get; set; }
        public Grade Grade { get; set; }
    }
}