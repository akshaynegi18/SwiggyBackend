using Microsoft.AspNetCore.Mvc;

namespace NotificationService.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly ILogger<NotificationController> _logger;

        public NotificationController(ILogger<NotificationController> logger)
        {
            _logger = logger;
        }

        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok("NotificationService is running 🚀");
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new { status = "healthy", service = "NotificationService", timestamp = DateTime.UtcNow });
        }

        [HttpPost("send")]
        public IActionResult Send([FromQuery] int userId, [FromQuery] string message)
        {
            try
            {
                // Simulate sending a notification (e.g., log to console)
                _logger.LogInformation("[NotificationService] Notification sent to user {UserId}: {Message}", userId, message);

                return Ok(new { Status = "Notification sent", UserId = userId, Message = message, Timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending notification to user {UserId}", userId);
                return StatusCode(500, "Error sending notification");
            }
        }
    }
}