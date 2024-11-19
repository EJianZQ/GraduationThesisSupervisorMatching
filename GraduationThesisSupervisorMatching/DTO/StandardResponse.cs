namespace GraduationThesisSupervisorMatching.DTO
{
    public class StandardResponse
    {
        public bool success {  get; set; }
        public string? message { get; set; }
        public object? Data { get; set; }
    }
}