namespace UserService.Model
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;  
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;  
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = "Customer";  
        public bool IsActive { get; set; } = true; 
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; } 
        public DateTime? LastLoginAt { get; set; } 
    }
}