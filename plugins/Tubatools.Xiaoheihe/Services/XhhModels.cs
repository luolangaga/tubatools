using System.Globalization;

namespace Tubatools.Xiaoheihe.Services;

internal sealed record XhhUser(
    string Id,
    string Name,
    string Avatar,
    int Level = 0,
    string Bio = "",
    long FollowCount = 0,
    long FanCount = 0,
    long LinkCount = 0)
{
    public string LevelText => Level > 0 ? $"Lv.{Level}" : "";
}

internal sealed class XhhPost
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Html { get; init; } = "";
    public string Text { get; init; } = "";
    public IReadOnlyList<string> Images { get; init; } = [];
    public string? VideoUrl { get; init; }
    public string? VideoThumb { get; init; }
    public bool HasVideo { get; init; }
    public string AuthorId { get; init; } = "";
    public string AuthorName { get; init; } = "";
    public string AuthorAvatar { get; init; } = "";
    public int AuthorLevel { get; init; }
    public string TopicId { get; init; } = "";
    public string TopicName { get; init; } = "";
    public long CommentCount { get; set; }
    public long LikeCount { get; set; }
    public long CoinCount { get; set; }
    public long CreateAt { get; init; }
    public string IpLocation { get; init; } = "";
    public string ShareUrl { get; init; } = "";
    public bool IsLiked { get; init; }
    public bool IsFavourited { get; init; }
    public bool IsCoined { get; init; }

    public string WebUrl => $"https://www.xiaoheihe.cn/app/bbs/link/{Id}";
    public string? Cover => Images.FirstOrDefault() ?? VideoThumb;
    public string TimeText => XhhFormat.Time(CreateAt);
    public string AuthorLevelText => AuthorLevel > 0 ? $"Lv.{AuthorLevel}" : "";
    public string CountsText
    {
        get
        {
            var parts = new List<string>();
            if (CommentCount > 0) parts.Add($"评论 {XhhFormat.Count(CommentCount)}");
            if (LikeCount > 0) parts.Add($"赞 {XhhFormat.Count(LikeCount)}");
            if (CoinCount > 0) parts.Add($"投币 {XhhFormat.Count(CoinCount)}");
            return string.Join(" · ", parts);
        }
    }
}

internal sealed class XhhComment
{
    public string Id { get; init; } = "";
    public string UserId { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Avatar { get; init; } = "";
    public int Level { get; init; }
    public string Text { get; init; } = "";
    public IReadOnlyList<string> Images { get; init; } = [];
    public int Floor { get; init; }
    public int ChildCount { get; init; }
    public long Likes { get; set; }
    public int SupportState { get; set; }
    public string IpLocation { get; init; } = "";
    public bool IsOwner { get; init; }
    public long CreateAt { get; init; }
    public string ReplyTo { get; init; } = "";
    public List<XhhComment> Children { get; } = [];

    public string TimeText => XhhFormat.Time(CreateAt);
    public string FloorText => Floor > 0 ? $"{Floor} 楼" : "";
    public string LevelText => Level > 0 ? $"Lv.{Level}" : "";
    public bool IsSupported => SupportState == 1;
}

internal sealed class XhhCommentPage
{
    public List<XhhComment> Comments { get; init; } = [];
    public int TotalFloor { get; init; }
    public int TotalPage { get; init; }
    public int Page { get; init; }
    public bool HasMore { get; init; }
    public string Sort { get; init; } = "hot";
}

internal sealed record XhhTopic(string Id, string Name, string Picture, long Hot);

internal sealed record XhhTopicGroup(string Title, IReadOnlyList<XhhTopic> Topics);

internal sealed class XhhGame
{
    public string AppId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Image { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Score { get; init; } = "";
    public string Price { get; init; } = "";
    public string ReleaseDate { get; init; } = "";
    public string Online { get; init; } = "";
    public string FollowState { get; init; } = "";
    public string GameType { get; init; } = "";
    public bool IsFree { get; init; }
    public long FollowCount { get; init; }

    public string PriceText => IsFree ? "免费" : string.IsNullOrWhiteSpace(Price) ? "价格未知" : $"¥{Price}";
    public string FollowText => FollowState == "following" ? "已关注" : "关注";
}

internal sealed record XhhGameMedia(string Type, string Thumbnail, string Url);

internal sealed class XhhGameDetail
{
    public XhhGame Game { get; init; } = new();
    public string Description { get; init; } = "";
    public IReadOnlyList<(string Label, string Value)> Info { get; init; } = [];
    public IReadOnlyList<XhhGameMedia> Media { get; init; } = [];
    public IReadOnlyList<string> Screenshots { get; init; } = [];
}

internal sealed record XhhSearchItem(
    string Type,
    XhhPost? Post = null,
    XhhGame? Game = null,
    XhhUser? User = null,
    XhhTopic? Topic = null,
    string Text = "");

internal sealed class XhhMessageItem
{
    public string Id { get; init; } = "";
    public int Kind { get; init; }
    public string Title { get; init; } = "";
    public string UserName { get; init; } = "";
    public string Avatar { get; init; } = "";
    public string Content { get; init; } = "";
    public long CreateAt { get; init; }
    public string PostId { get; init; } = "";
    public string TimeText => XhhFormat.Time(CreateAt);
}

internal sealed class XhhSignState
{
    public bool SignedToday { get; init; }
    public int SignDays { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<string> Tasks { get; init; } = [];
}

internal static class XhhFormat
{
    public static string Time(long seconds)
    {
        if (seconds <= 0) return "";
        DateTime time = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
        TimeSpan diff = DateTime.Now - time;
        if (diff.TotalSeconds < 60) return "刚刚";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} 分钟前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} 小时前";
        if (diff.TotalDays < 30) return $"{(int)diff.TotalDays} 天前";
        return time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static string Count(long value) => value switch
    {
        >= 100_000_000 => $"{value / 100_000_000d:0.#}亿",
        >= 10_000 => $"{value / 10_000d:0.#}万",
        _ => value.ToString(CultureInfo.InvariantCulture)
    };

    public static string Clip(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
