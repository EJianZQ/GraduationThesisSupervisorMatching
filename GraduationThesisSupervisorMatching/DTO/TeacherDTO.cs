namespace GraduationThesisSupervisorMatching.DTO
{
    public class TeacherDTO
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string GradeName { get; set; }
        public LevelCapacitiesDTO LevelCapacities { get; set; }
        public int LevelAvailableCapacity { get; set; }
        public List<StudentSimpleDTO>? BelongingStudents { get; set; }
        public int PreferenceCount { get; set; }
        public string Description { get; set; }
        public bool IsAcceptTopStudent { get; set; }
        public bool IsChosenByTopStudent { get; set; }
    }

    public class LevelCapacitiesDTO
    {
        public int UpperLevelCapacity { get; set; }
        public int MiddleLevelCapacity { get; set; }
        public int LowerLevelCapacity { get; set; }
    }
}
