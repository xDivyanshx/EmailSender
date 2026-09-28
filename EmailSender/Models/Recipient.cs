namespace EmailSender.Models
{
    /// <summary>
    /// Represents a single recipient parsed from the Master CSV file.
    /// </summary>
    public class Recipient
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Organization { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string AttachmentFileName { get; set; } = string.Empty;
        public string Template { get; set; } = string.Empty;
        public string PresetName { get; set; } = string.Empty;
        public string HtmlBody { get; set; } = string.Empty;
        public string AttachmentDisplayName { get; set; } = string.Empty;
        public List<string> Attachments { get; set; } = [];

        /// <summary>
        /// Helper to get just the first name for email personalization.
        /// </summary>
        public string FirstName => string.IsNullOrWhiteSpace(Name) ? "" : Name.Split(' ')[0];

        /// <summary>
        /// Helper to get the first word of the organization to match template names.
        /// </summary>
        public string OrganizationKey => string.IsNullOrWhiteSpace(Organization) ? "" : Organization.Split(' ')[0];
    }
}
