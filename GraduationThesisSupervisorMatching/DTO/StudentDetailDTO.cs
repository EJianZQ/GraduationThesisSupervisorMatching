namespace GraduationThesisSupervisorMatching.DTO
{
    public class StudentDetailDTO
    {
        public long Id { get; set; }
        public string StudentID { get; set; }
        public string Name { get; set; }
        public decimal GPA { get; set; }
        public int GPARank { get; set; }
        public string Level { get; set; }
        public int LevelRank { get; set; }
        public AssignedTeacherDTO AssignedTeacher { get; set; }
        public List<string> PreferenceTeacherNames { get; set; }
        public long GradeId { get; set; }
        public string GradeName { get; set; }
    }

    public class AssignedTeacherDTO
    {
        public long Id { get; set; }
        public string Name { get; set; }
    }
}
