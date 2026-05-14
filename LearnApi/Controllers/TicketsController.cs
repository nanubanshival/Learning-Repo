using LearnApi.Models;
using Microsoft.AspNetCore.Mvc;

namespace LearnApi.Controllers;

[ApiController]
[Route("api/tickets")]
public class TicketsController : ControllerBase
{
    // In-memory "database" — just a static list for now.
    // Lives as long as the process runs; restart = data resets.
    private static readonly List<Ticket> _tickets = new()
    {
        new Ticket { Id = 1, Title = "Printer not working", Description = "Paper jam in tray 2", Priority = "High",   Status = "Open" },
        new Ticket { Id = 2, Title = "Email slow",          Description = "Outlook taking 30s to send", Priority = "Medium", Status = "InProgress" },
        new Ticket { Id = 3, Title = "Mouse broken",        Description = "Left-click double-fires",     Priority = "Low",    Status = "Resolved" },
    };

    // GET /api/tickets
    [HttpGet]
    public IEnumerable<Ticket> GetAll()
    {
        return _tickets;
    }

    // GET /api/tickets/42
    [HttpGet("{id}")]
    public ActionResult<Ticket> GetById(int id)
    {
        var ticket = _tickets.FirstOrDefault(t => t.Id == id);
        if (ticket == null) return NotFound();
        return ticket;
    }

    // POST /api/tickets
    [HttpPost]
    public ActionResult<Ticket> Create([FromBody] Ticket ticket)
    {
        ticket.Id = _tickets.Count == 0 ? 1 : _tickets.Max(t => t.Id) + 1;
        ticket.CreatedAt = DateTime.UtcNow;
        _tickets.Add(ticket);
        return CreatedAtAction(nameof(GetById), new { id = ticket.Id }, ticket);
    }

    // PUT /api/tickets/42
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] Ticket updated)
    {
        var existing = _tickets.FirstOrDefault(t => t.Id == id);
        if (existing == null) return NotFound();

        existing.Title       = updated.Title;
        existing.Description = updated.Description;
        existing.Priority    = updated.Priority;
        existing.Status      = updated.Status;
        return NoContent();
    }

    // DELETE /api/tickets/42
    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        var ticket = _tickets.FirstOrDefault(t => t.Id == id);
        if (ticket == null) return NotFound();

        _tickets.Remove(ticket);
        return NoContent();
    }
}
