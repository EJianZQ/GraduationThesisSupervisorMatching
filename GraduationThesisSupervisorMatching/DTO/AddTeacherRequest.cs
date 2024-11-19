namespace GraduationThesisSupervisorMatching.DTO
{
    public class AddTeacherRequest
    {
        public string Name { get; set; }
        public int MaxCapacity { get; set; }
        public long GradeId { get; set; }
        public string Description { get; set; }
        public bool AcceptsTopStudent { get; set; }
        public int UpperLevelCapacity { get; set; }
        public int MiddleLevelCapacity { get; set; }
        public int LowerLevelCapacity { get; set; }
    }
}