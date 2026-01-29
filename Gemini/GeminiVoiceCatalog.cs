using System.Collections.Generic;
using System.Linq;

namespace SipAiGateway;

public class VoiceInfo
{
    public string Name { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RecommendedUse { get; set; } = string.Empty;

    public override string ToString() => $"{Name} ({Gender}) - {Description}";
}

public static class GeminiVoiceCatalog
{
    public const string DefaultVoice = "Puck";

    public static readonly IReadOnlyList<VoiceInfo> Voices = new List<VoiceInfo>
    {
        new VoiceInfo
        {
            Name = "Puck",
            Gender = "Male",
            Description = "Energetic, enthusiastic, and slightly higher-pitched. The default 'helpful assistant' voice.",
            RecommendedUse = "General assistance, lively conversation"
        },
        new VoiceInfo
        {
            Name = "Charon",
            Gender = "Male",
            Description = "Deep, authoritative, and calm. A grounded voice that sounds experienced and steady.",
            RecommendedUse = "News reading, formal assistance, serious topics"
        },
        new VoiceInfo
        {
            Name = "Kore",
            Gender = "Female",
            Description = "Balanced, professional, and firm. A standard, neutral voice that is clear and direct.",
            RecommendedUse = "Professional interaction, technical support"
        },
        new VoiceInfo
        {
            Name = "Fenrir",
            Gender = "Male",
            Description = "Intense and enthusiastic. A deeper but highly energetic voice that conveys excitement.",
            RecommendedUse = "Storytelling, high-energy topics"
        },
        new VoiceInfo
        {
            Name = "Aoede",
            Gender = "Female",
            Description = "Composed, professional, and confident. A smooth voice that sounds capable and mature.",
            RecommendedUse = "Business, formal inquiries"
        },
        new VoiceInfo
        {
            Name = "Algenib",
            Gender = "Male",
            Description = "Deep, warm, and gravelly. Projects a sense of friendly authority and experience.",
            RecommendedUse = "Narration, mature character roles"
        },
        new VoiceInfo
        {
            Name = "Zephyr",
            Gender = "Female",
            Description = "Bright, perky, and high-energy. Projects youthfulness and positivity.",
            RecommendedUse = "Friendly chat, upbeat interactions"
        },
        new VoiceInfo
        {
            Name = "Orus",
            Gender = "Male",
            Description = "Firm and confident. A mid-range voice that sounds decisive.",
            RecommendedUse = "Instructional content, clear guidance"
        },
        new VoiceInfo
        {
            Name = "Autonoe",
            Gender = "Female",
            Description = "Mature, resonant, and thoughtful. Conveys wisdom with a measured pace.",
            RecommendedUse = "Reading, philosophical discussion"
        },
        new VoiceInfo
        {
            Name = "Umbriel",
            Gender = "Male",
            Description = "Easy-going and casual. A relatable, mid-pitched voice that sounds relaxed.",
            RecommendedUse = "Casual conversation, friendly advice"
        },
        new VoiceInfo
        {
            Name = "Erinome",
            Gender = "Female",
            Description = "Clear and articulate. A precise voice that focuses on intelligibility.",
            RecommendedUse = "Educational content, explanations"
        },
        new VoiceInfo
        {
            Name = "Laomedeia",
            Gender = "Female",
            Description = "Inquisitive and engaging. Similar to Aoede but with a slightly more conversational tone.",
            RecommendedUse = "Interviews, Q&A sessions"
        },
        new VoiceInfo
        {
            Name = "Schedar",
            Gender = "Male",
            Description = "Down-to-earth and informal. Sounds like a friendly neighbor.",
            RecommendedUse = "Casual chat, informal updates"
        },
        new VoiceInfo
        {
            Name = "Achird",
            Gender = "Female",
            Description = "Youthful and breathy. A lighter, friendly voice that sounds approachable.",
            RecommendedUse = "Casual assistance, friendly greetings"
        },
        new VoiceInfo
        {
            Name = "Sadachbia",
            Gender = "Male",
            Description = "Lively and active. A dynamic voice that avoids sounding monotone.",
            RecommendedUse = "Engaging content, alerts"
        },
        new VoiceInfo
        {
            Name = "Zubenelgenubi",
            Gender = "Male",
            Description = "Deep and resonant. A powerful voice that commands attention.",
            RecommendedUse = "Announcements, dramatic reading"
        }
    };

    public static VoiceInfo GetVoice(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Voices.First(v => v.Name == DefaultVoice);
        }

        return Voices.FirstOrDefault(v => v.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
               ?? Voices.First(v => v.Name == DefaultVoice);
    }
}
