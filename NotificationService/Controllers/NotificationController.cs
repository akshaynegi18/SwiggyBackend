using Microsoft.AspNetCore.Mvc;

namespace NotificationService.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class NotificationController : ControllerBase
    {
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok("NotificationService is running 🚀");
        }

        [HttpPost("send")]
        public IActionResult Send([FromQuery] int userId, [FromQuery] string message)
        {
            // Simulate sending a notification (e.g., log to console)
            Console.WriteLine($"[NotificationService] Notification sent to user {userId}: {message}");
            return Ok(new { Status = "Notification sent", UserId = userId, Message = message });
        }
    }
}