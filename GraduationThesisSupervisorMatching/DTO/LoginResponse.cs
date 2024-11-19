namespace GraduationThesisSupervisorMatching.DTO
{
    public class LoginResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Token { get; set; }
        public string? Role { get; set; }
        public string? Name { get; set; }
        public string? StudentId { get; set; }
        public string? GPA { get; set; }
        public int? GPARank { get; set; }
        public string? GradeName { get; set; }
        public long? GradeId { get; set; }
        public List<string>? PreferTeachersName { get; set; }
        public string? AssignTeacherName { get; set; }
    }
}