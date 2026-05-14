namespace LearnApi.Models;

public class Ticket
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Priority { get; set; } = "Medium";  // Low / Medium / High
    public string Status { get; set; } = "Open";      // Open / InProgress / Resolved / Closed
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
