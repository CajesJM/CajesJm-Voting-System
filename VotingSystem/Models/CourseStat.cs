namespace VotingSystem.Models
{
    public class CourseStat
    {
        public string CourseName { get; set; }
        public int TotalStudents { get; set; }
        public int VotedStudents { get; set; }
        public double Percentage { get; set; }
    }
}