using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tubatools.Xiaoheihe.Services;

internal static partial class XhhParser
{
    private static readonly Regex ImageTag = new(@"<img\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OriginalAttr = new(@"data-(?:original|src|url)\s*=\s*""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SrcAttr = new(@"\bsrc\s*=\s*""([^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.Compiled);

    internal static XhhUser ParseUser(JsonElement user)
    {
        if (user.ValueKind != JsonValueKind.Object) return new XhhUser("", "", "");

        string id = user.FirstStr("userid", "user_id", "heybox_id", "id");
        string name = user.FirstStr("username", "nickname", "name");
        string avatar = user.FirstStr("avatar", "avartar", "avatar_url", "head_image");
        int level = (int)user.Long("level");
        if (level == 0) level = (int)user.Obj("level_info").Long("level");
        if (level == 0)
        {
            string levelText = user.Str("level");
            if (levelText.StartsWith("Lv.", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(levelText[3..], out int parsed)) level = parsed;
        }

        string bio = user.FirstStr("description", "bio", "signature", "intro");
        JsonElement bbs = user.Obj("bbs_info");
        long follows = user.FirstLong("following_num", "follow_num", "follow_count");
        if (follows == 0) follows = bbs.FirstLong("follow_num", "following_num", "follow_count");
        long fans = user.FirstLong("follower_num", "fans_num", "fans_count", "fans");
        if (fans == 0) fans = bbs.FirstLong("fan_num", "follower_num", "fans_num", "fans_count", "fans");
        long links = user.FirstLong("link_num", "post_num", "article_num");
        if (links == 0) links = bbs.FirstLong("post_link_num", "link_num", "post_num", "article_num");
        return new XhhUser(id, name, avatar, level, StripHtml(bio), follows, fans, links);
    }

    internal static XhhPost ParsePost(JsonElement link, JsonElement? fallbackUser = null, long? fallbackTime = null)
    {
        if (link.ValueKind != JsonValueKind.Object) return new XhhPost();

        JsonElement user = link.Obj("user");
        if (user.ValueKind != JsonValueKind.Object && fallbackUser is { } backup) user = backup;
        XhhUser author = ParseUser(user);
        if (string.IsNullOrWhiteSpace(author.Name) && fallbackUser is { } fallback &&
            fallback.ValueKind == JsonValueKind.Object)
            author = ParseUser(fallback);
        if (string.IsNullOrWhiteSpace(author.Name) && !string.IsNullOrWhiteSpace(link.FirstStr("link_user", "username")))
            author = new XhhUser(link.FirstStr("link_userid", "userid"), link.FirstStr("link_user", "username"), "", 0);

        JsonElement topic = link.Arr("topics").FirstOrDefault();
        string topicId = topic.ValueKind == JsonValueKind.Object ? topic.FirstStr("topic_id", "id") : "";
        string topicName = topic.ValueKind == JsonValueKind.Object ? topic.Str("name") : "";

        string rawText = link.Str("text");
        var content = ParseContent(rawText);

        var images = ReadUrls(link, "imgs");
        if (images.Count == 0) images = ReadUrls(link, "thumbs");
        if (images.Count == 0) images = content.Images;
        string cover = link.FirstStr("image", "thumb", "link_img", "video_thumb");
        if (!string.IsNullOrWhiteSpace(cover) && !images.Contains(cover)) images.Insert(0, cover);

        string videoUrl = link.FirstStr("video_url", "video");
        string videoThumb = link.FirstStr("video_thumb", "video_poster");
        if (string.IsNullOrWhiteSpace(videoUrl) && link.Obj("video") is { ValueKind: JsonValueKind.Object } video)
        {
            videoUrl = video.FirstStr("url", "video_url");
            videoThumb = video.FirstStr("poster", "thumb", "cover");
        }

        string summary = link.FirstStr("description", "link_desc");
        if (string.IsNullOrWhiteSpace(summary)) summary = content.PlainText;

        long createAt = link.FirstLong("create_at", "timestamp", "modify_at");
        if (createAt <= 0 && fallbackTime is > 0) createAt = fallbackTime.Value;

        string id = link.FirstStr("linkid", "link_id", "id");
        return new XhhPost
        {
            Id = id,
            Title = WebUtility.HtmlDecode(link.FirstStr("title", "link_title")).Trim(),
            Summary = XhhFormat.Clip(StripHtml(summary), 180),
            Html = content.Html,
            Text = content.PlainText,
            Images = images,
            VideoUrl = string.IsNullOrWhiteSpace(videoUrl) ? null : videoUrl,
            VideoThumb = string.IsNullOrWhiteSpace(videoThumb) ? null : videoThumb,
            HasVideo = link.Bool("has_video") || !string.IsNullOrWhiteSpace(videoUrl),
            AuthorId = author.Id,
            AuthorName = author.Name,
            AuthorAvatar = author.Avatar,
            AuthorLevel = author.Level,
            TopicId = topicId,
            TopicName = topicName,
            CommentCount = link.Long("comment_num"),
            LikeCount = link.Long("up"),
            CoinCount = link.Long("link_award_num"),
            CreateAt = createAt,
            IpLocation = link.Str("ip_location"),
            ShareUrl = link.Str("share_url"),
            IsLiked = link.Bool("is_up") || link.Bool("is_like") || link.Bool("has_up"),
            IsFavourited = link.Bool("is_favour") || link.Bool("is_fav") || link.Bool("has_favour"),
            IsCoined = link.Bool("is_award_link") || link.Bool("is_award")
        };
    }

    internal static (string Html, string PlainText, List<string> Images) ParseContent(string? text)
    {
        string html = "";
        var images = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return ("", "", images);

        string trimmed = text.TrimStart();
        if (trimmed.StartsWith('['))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement element in document.RootElement.EnumerateArray())
                    {
                        if (element.ValueKind != JsonValueKind.Object) continue;
                        string type = element.Str("type");
                        if (type is "text" or "html")
                        {
                            string part = element.Str("text");
                            if (string.IsNullOrWhiteSpace(part)) part = element.Str("html");
                            if (!string.IsNullOrWhiteSpace(part))
                            {
                                html += part;
                                images.AddRange(ExtractImages(part));
                            }
                        }
                        else if (type == "img")
                        {
                            string url = element.FirstStr("url", "src");
                            if (!string.IsNullOrWhiteSpace(url)) images.Add(url);
                        }
                        else if (type == "video")
                        {
                            string url = element.FirstStr("url", "video_url");
                            if (!string.IsNullOrWhiteSpace(url)) images.Add(url);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                html = text;
            }
        }
        else
        {
            html = text;
        }

        if (string.IsNullOrWhiteSpace(html)) html = text;
        images.AddRange(ExtractImages(html));
        return (html, StripHtml(html), Deduplicate(images));
    }

    internal static List<string> ExtractImages(string? html)
    {
        var images = new List<string>();
        if (string.IsNullOrWhiteSpace(html)) return images;

        foreach (Match tag in ImageTag.Matches(html))
        {
            string value = "";
            Match original = OriginalAttr.Match(tag.Value);
            if (original.Success) value = original.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                Match src = SrcAttr.Match(tag.Value);
                if (src.Success) value = src.Groups[1].Value;
            }
            if (string.IsNullOrWhiteSpace(value)) continue;
            value = WebUtility.HtmlDecode(value);
            if (IsEmojiOrAvatar(value)) continue;
            images.Add(value);
        }
        return Deduplicate(images);
    }

    internal static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        string text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</(?:p|div|li|tr|h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
        text = ImageTag.Replace(text, "");
        text = AnyTag.Replace(text, "");
        text = WebUtility.HtmlDecode(text);
        text = text.Replace('\u00A0', ' ');
        text = Regex.Replace(text, @"[ \t]+\n", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    internal static IReadOnlyList<XhhComment> ParseSubComments(JsonElement result)
    {
        var comments = new List<XhhComment>();
        foreach (JsonElement item in result.Arr("comments"))
        {
            IReadOnlyList<JsonElement> nested = item.Arr("comment");
            if (nested.Count > 0)
            {
                foreach (JsonElement child in nested) comments.Add(ParseComment(child));
            }
            else
            {
                comments.Add(ParseComment(item));
            }
        }
        if (comments.Count == 0)
            foreach (JsonElement item in result.Arr("comment")) comments.Add(ParseComment(item));
        return comments;
    }

    internal static XhhCommentPage ParseCommentPage(JsonElement result, int page)
    {
        var comments = new List<XhhComment>();
        foreach (JsonElement floor in result.Arr("comments"))
        {
            IReadOnlyList<JsonElement> group = floor.Arr("comment");
            if (group.Count > 0)
            {
                XhhComment root = ParseComment(group[0]);
                for (int i = 1; i < group.Count; i++) root.Children.Add(ParseComment(group[i]));
                comments.Add(root);
            }
            else if (floor.ValueKind == JsonValueKind.Object)
            {
                comments.Add(ParseComment(floor));
            }
        }

        int totalFloor = (int)result.Long("total_floor_num");
        int totalPage = (int)result.Long("total_page");
        bool hasMore = result.Bool("has_more_floors") || (totalPage > 0 && page < totalPage);
        return new XhhCommentPage
        {
            Comments = comments,
            TotalFloor = totalFloor,
            TotalPage = totalPage,
            Page = page,
            HasMore = hasMore,
            Sort = result.Str("sort_filter") is { Length: > 0 } sort ? sort : "hot"
        };
    }

    internal static XhhComment ParseComment(JsonElement comment)
    {
        if (comment.ValueKind != JsonValueKind.Object) return new XhhComment();
        XhhUser user = ParseUser(comment.Obj("user"));
        string replyTo = ParseUser(comment.Obj("replyuser")).Name;
        var images = ReadUrls(comment, "imgs");
        if (images.Count == 0) images = ReadUrls(comment, "images");
        return new XhhComment
        {
            Id = comment.FirstStr("commentid", "comment_id", "id"),
            UserId = user.Id,
            UserName = user.Name,
            Avatar = user.Avatar,
            Level = user.Level,
            Text = StripHtml(comment.Str("text")),
            Images = images,
            Floor = (int)comment.Long("floor_num"),
            ChildCount = (int)comment.Long("child_num"),
            Likes = comment.Long("up"),
            SupportState = (int)comment.Long("is_support"),
            IpLocation = comment.Str("ip_location"),
            IsOwner = comment.Bool("is_link_owner"),
            CreateAt = comment.Long("create_at"),
            ReplyTo = replyTo
        };
    }

    internal static IReadOnlyList<XhhTopicGroup> ParseTopicGroups(JsonElement result)
    {
        var groups = new List<XhhTopicGroup>();
        foreach (string key in new[] { "recommend_for_user_topics", "latest_hot_topics", "subscribed_topics" })
        {
            JsonElement group = result.Obj(key);
            if (group.ValueKind == JsonValueKind.Object)
            {
                IReadOnlyList<XhhTopic> topics = ParseTopics(group.Arr("children"));
                if (topics.Count > 0)
                    groups.Add(new XhhTopicGroup(group.Str("name") is { Length: > 0 } name ? name : key, topics));
            }
        }

        IReadOnlyList<JsonElement> categories = result.Arr("categories");
        if (categories.Count > 0)
        {
            IReadOnlyList<XhhTopic> topics = ParseTopics(categories);
            if (topics.Count > 0) groups.Add(new XhhTopicGroup("全部分类", topics));
        }

        return groups;
    }

    internal static IReadOnlyList<XhhTopic> ParseTopicList(JsonElement result, params string[] keys)
    {
        foreach (string key in keys)
        {
            IReadOnlyList<JsonElement> array = result.Arr(key);
            if (array.Count > 0) return ParseTopics(array);
        }
        return [];
    }

    private static IReadOnlyList<XhhTopic> ParseTopics(IReadOnlyList<JsonElement> elements)
    {
        var topics = new List<XhhTopic>();
        foreach (JsonElement element in elements)
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            IReadOnlyList<JsonElement> children = element.Arr("children");
            if (children.Count > 0)
            {
                topics.AddRange(ParseTopics(children));
                continue;
            }
            string id = element.FirstStr("topic_id", "id", "hashtag_id");
            string name = element.FirstStr("name", "title");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
            string picture = element.FirstStr("small_pic_url", "pic_url", "icon", "image");
            long hot = element.Obj("hot").Long("raw_hot_value");
            if (hot == 0) hot = (long)element.Long("hot_value_v2");
            topics.Add(new XhhTopic(id, name, picture, hot));
        }
        return topics.GroupBy(t => t.Id).Select(g => g.First()).ToList();
    }

    internal static IReadOnlyList<XhhPost> ParsePostList(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Array)
            return result.EnumerateArray().Select(item => ParsePost(item)).Where(p => p.Id.Length > 0).ToList();

        JsonElement fallbackUser = result.Obj("user");
        foreach (string key in new[] { "links", "link_list", "post_links", "list", "items", "history_visit" })
        {
            IReadOnlyList<JsonElement> array = result.Arr(key);
            if (array.Count == 0) continue;
            var posts = new List<XhhPost>();
            foreach (JsonElement item in array)
            {
                JsonElement link = item.Obj("link");
                if (link.ValueKind != JsonValueKind.Object) link = item.Obj("link_info");
                if (link.ValueKind != JsonValueKind.Object) link = item.Obj("content");
                if (link.ValueKind != JsonValueKind.Object) link = item;
                long outerTime = item.FirstLong("timestamp", "create_at");
                XhhPost post = ParsePost(link, fallbackUser.ValueKind == JsonValueKind.Object ? fallbackUser : null,
                    outerTime > 0 ? outerTime : null);
                if (post.Id.Length > 0) posts.Add(post);
            }
            if (posts.Count > 0) return posts;
        }
        return [];
    }

    internal static IReadOnlyList<XhhSearchItem> ParseSearchItems(JsonElement result)
    {
        var items = new List<XhhSearchItem>();
        foreach (JsonElement item in result.Arr("items"))
        {
            string type = item.Str("type");
            JsonElement info = item.Obj("info");
            switch (type)
            {
                case "link":
                {
                    XhhPost post = ParsePost(info);
                    if (post.Id.Length > 0) items.Add(new XhhSearchItem(type, Post: post));
                    break;
                }
                case "game":
                {
                    XhhGame game = ParseGame(info);
                    if (game.AppId.Length > 0) items.Add(new XhhSearchItem(type, Game: game));
                    break;
                }
                case "user":
                {
                    XhhUser user = ParseUser(info);
                    if (user.Id.Length > 0) items.Add(new XhhSearchItem(type, User: user));
                    break;
                }
                case "topic":
                case "hashtag":
                {
                    JsonElement topic = info.Obj("topic");
                    if (topic.ValueKind != JsonValueKind.Object) topic = info;
                    string id = topic.FirstStr("topic_id", "hashtag_id", "id");
                    string name = topic.FirstStr("name", "title");
                    if (name.Length > 0) items.Add(new XhhSearchItem(type, Topic: new XhhTopic(id, name, topic.FirstStr("pic_url", "image"), 0)));
                    break;
                }
                case "new_game_list":
                {
                    foreach (JsonElement gameElement in info.Arr("game_list"))
                    {
                        XhhGame game = ParseGame(gameElement);
                        if (game.AppId.Length > 0) items.Add(new XhhSearchItem("game", Game: game));
                    }
                    break;
                }
                case "space":
                case "filter_tag":
                case "relevant_search":
                    break;
                default:
                {
                    string text = info.FirstStr("name", "title", "description");
                    if (text.Length > 0) items.Add(new XhhSearchItem(type, Text: text));
                    break;
                }
            }
        }
        return items;
    }

    internal static IReadOnlyList<XhhGame> ParseGames(JsonElement result)
    {
        foreach (string key in new[] { "game_list", "games", "list", "items" })
        {
            IReadOnlyList<JsonElement> array = result.Arr(key);
            if (array.Count == 0) continue;
            var games = new List<XhhGame>();
            foreach (JsonElement element in array)
            {
                JsonElement game = element.Obj("game");
                if (game.ValueKind != JsonValueKind.Object) game = element;
                XhhGame parsed = ParseGame(game);
                if (parsed.AppId.Length > 0) games.Add(parsed);
            }
            if (games.Count > 0) return games;
        }

        if (result.ValueKind == JsonValueKind.Array)
        {
            return result.EnumerateArray().Select(ParseGame).Where(g => g.AppId.Length > 0).ToList();
        }
        return [];
    }

    internal static XhhGame ParseGame(JsonElement game)
    {
        if (game.ValueKind != JsonValueKind.Object) return new XhhGame();
        string price = game.Obj("price").FirstStr("current", "initial");
        bool isFree = game.Bool("is_free") || game.Obj("price").Bool("is_free");
        return new XhhGame
        {
            AppId = FirstNumericId(game, "appid", "steam_appid", "app_id", "gameid", "game_id", "id"),
            Name = game.FirstStr("name", "title"),
            Image = game.FirstStr("image", "head_image_616_353", "square_head_image", "background", "cover"),
            Icon = game.FirstStr("appicon", "icon"),
            Score = game.FirstStr("score", "score_desc"),
            Price = price,
            ReleaseDate = game.FirstStr("release_date", "release_date_desc"),
            Online = game.Str("online_player"),
            FollowState = game.Str("follow_state"),
            GameType = game.Str("game_type"),
            IsFree = isFree,
            FollowCount = game.Long("follow_num")
        };
    }

    private static string FirstNumericId(JsonElement element, params string[] keys)
    {
        foreach (string key in keys)
        {
            string value = element.Str(key).Trim();
            if (long.TryParse(value, out long id) && id > 0) return value;
        }
        return "";
    }

    internal static XhhGameDetail ParseGameDetail(JsonElement result)
    {
        JsonElement gameElement = result.Obj("game");
        if (gameElement.ValueKind != JsonValueKind.Object) gameElement = result;
        XhhGame game = ParseGame(gameElement);

        string description = gameElement.FirstStr("description", "introduction", "game_introduction", "desc");
        if (string.IsNullOrWhiteSpace(description)) description = result.FirstStr("description", "introduction");
        if (string.IsNullOrWhiteSpace(description)) description = result.FirstStr("about_the_game", "short_desc");
        if (string.IsNullOrWhiteSpace(description)) description = gameElement.FirstStr("about_the_game", "short_desc");
        description = XhhFormat.Clip(StripHtml(description), 4000);

        var info = new List<(string Label, string Value)>();
        foreach (JsonElement menu in result.Arr("menu_v2"))
        {
            string label = menu.FirstStr("title", "name", "label");
            string value = menu.FirstStr("content", "value", "text");
            if (label.Length > 0 && value.Length > 0) info.Add((label, StripHtml(value)));
        }
        foreach (JsonElement data in result.Arr("game_data"))
        {
            string label = data.FirstStr("desc", "title", "name");
            string value = data.FirstStr("value", "content");
            if (label.Length > 0 && value.Length > 0) info.Add((label, StripHtml(value)));
        }
        foreach (var (label, keys) in new (string, string[])[]
                 {
                     ("开发商", ["developer", "developers"]),
                     ("发行商", ["publisher", "publishers"]),
                     ("发行日期", ["release_date", "release_date_desc"]),
                     ("平台", ["platforms_desc", "platform"]),
                     ("游戏类型", ["game_type", "genres"])
                 })
        {
            foreach (string key in keys)
            {
                string value = gameElement.Str(key);
                if (value.Length > 0)
                {
                    info.Add((label, StripHtml(value)));
                    break;
                }
            }
        }

        var media = new List<XhhGameMedia>();
        foreach (JsonElement item in result.Arr("screenshots"))
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value)) media.Add(new XhhGameMedia("image", value, value));
                continue;
            }
            if (item.ValueKind != JsonValueKind.Object) continue;
            string type = item.Str("type");
            string thumb = item.FirstStr("thumbnail", "thumb", "poster", "image");
            string url = item.FirstStr("url", "src", "video_url");
            if (string.IsNullOrWhiteSpace(thumb)) thumb = url;
            if (string.IsNullOrWhiteSpace(url)) url = thumb;
            if (string.IsNullOrWhiteSpace(thumb)) continue;
            media.Add(new XhhGameMedia(type == "movie" ? "movie" : "image", thumb, url));
        }
        if (media.Count == 0)
        {
            foreach (JsonElement item in result.Arr("game_media"))
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    string type = item.Str("type");
                    string thumb = item.FirstStr("thumbnail", "thumb", "poster", "image");
                    string url = item.FirstStr("url", "src", "video_url");
                    if (string.IsNullOrWhiteSpace(thumb)) thumb = url;
                    if (string.IsNullOrWhiteSpace(url)) url = thumb;
                    if (!string.IsNullOrWhiteSpace(thumb))
                        media.Add(new XhhGameMedia(type == "movie" ? "movie" : "image", thumb, url));
                }
                else if (item.ValueKind == JsonValueKind.String)
                {
                    string? value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) media.Add(new XhhGameMedia("image", value, value));
                }
            }
        }
        if (media.Count == 0)
        {
            foreach (string url in ReadUrls(result, "screenshots"))
                media.Add(new XhhGameMedia("image", url, url));
        }
        if (media.Count == 0)
        {
            foreach (string url in ReadUrls(gameElement, "screenshots"))
                media.Add(new XhhGameMedia("image", url, url));
        }
        media = media.Take(12).ToList();
        var screenshots = media.Where(m => m.Type != "movie").Select(m => m.Url).ToList();

        return new XhhGameDetail
        {
            Game = game,
            Description = description,
            Info = info.GroupBy(i => i.Label).Select(g => g.First()).Take(10).ToList(),
            Media = media,
            Screenshots = screenshots
        };
    }

    internal static string ParseGameIntroduction(JsonElement result)
    {
        var parts = new List<string>();
        string about = StripHtml(result.Str("about_the_game"));
        if (!string.IsNullOrWhiteSpace(about)) parts.Add(about);

        JsonElement requirement = result.Obj("pc_requirements");
        string minimum = StripHtml(requirement.Str("minimum"));
        if (!string.IsNullOrWhiteSpace(minimum)) parts.Add("最低配置\n" + minimum);
        string recommended = StripHtml(requirement.Str("recommended"));
        if (!string.IsNullOrWhiteSpace(recommended)) parts.Add("推荐配置\n" + recommended);
        return string.Join("\n\n", parts);
    }

    internal static IReadOnlyList<XhhMessageItem> ParseMessages(JsonElement result, int kind)
    {
        var messages = new List<XhhMessageItem>();
        foreach (string key in new[] { "messages", "list", "message_list", "items" })
        {
            IReadOnlyList<JsonElement> array = result.Arr(key);
            if (array.Count == 0) continue;
            foreach (JsonElement element in array)
            {
                JsonElement user = element.Obj("user_a");
                if (user.ValueKind != JsonValueKind.Object) user = element.Obj("user_b");
                if (user.ValueKind != JsonValueKind.Object) user = element.Arr("user_as").FirstOrDefault();
                if (user.ValueKind != JsonValueKind.Object) user = element.Obj("user");
                XhhUser author = ParseUser(user);
                string title = element.FirstStr("link_title", "title");
                string content = element.FirstStr("comment_a_text", "comment_b_text", "link_desc", "text", "content", "message");
                if (content.Length == 0)
                {
                    JsonElement comment = element.Obj("comment");
                    if (comment.ValueKind == JsonValueKind.Object) content = comment.Str("text");
                }
                string postId = element.FirstStr("linkid", "link_id");
                if (postId.Length == 0)
                {
                    JsonElement link = element.Obj("link");
                    if (link.ValueKind == JsonValueKind.Object) postId = link.FirstStr("linkid", "link_id");
                }
                string avatar = author.Avatar;
                if (avatar.Length == 0) avatar = element.FirstStr("link_img", "avatar");
                int messageType = (int)element.Long("message_type");
                if (title.Length == 0)
                    title = messageType switch
                    {
                        1 => "回复了你",
                        3 => "赞与收藏",
                        _ => kind switch { 0 => "有人回复了你", 1 => "收到新的赞与收藏", _ => "消息中心" }
                    };
                messages.Add(new XhhMessageItem
                {
                    Id = element.FirstStr("message_id", "id", "msg_id"),
                    Kind = kind,
                    Title = title,
                    UserName = author.Name,
                    Avatar = avatar,
                    Content = StripHtml(content),
                    CreateAt = element.FirstLong("timestamp", "create_at", "time"),
                    PostId = postId
                });
            }
            if (messages.Count > 0) return messages;
        }

        if (kind == 2)
        {
            foreach (JsonElement header in result.Arr("header"))
            {
                JsonElement user = header.Obj("message_id");
                XhhUser author = ParseUser(user);
                string entry = header.Str("entry");
                string title = entry switch
                {
                    "comment" => "回复我的",
                    "award" => "赞与收藏",
                    "follow" => "新增关注",
                    "system" or "official" => "系统消息",
                    _ => "消息中心"
                };
                int count = (int)header.Long("count");
                messages.Add(new XhhMessageItem
                {
                    Id = "header:" + entry,
                    Kind = kind,
                    Title = title,
                    UserName = author.Name,
                    Avatar = author.Avatar,
                    Content = count > 0 ? $"{XhhFormat.Count(count)} 条未读" : "暂无未读",
                    CreateAt = header.FirstLong("timestamp", "create_at"),
                    PostId = ""
                });
            }
        }
        return messages;
    }

    internal static XhhSignState ParseSignState(JsonElement result, JsonElement? taskList = null)
    {
        var tasks = new List<string>();
        bool signedByTask = false;
        int daysByTask = 0;
        if (taskList is { } list)
        {
            foreach (JsonElement group in list.Arr("task_list"))
            {
                foreach (JsonElement task in group.Arr("tasks"))
                {
                    string type = task.Str("type");
                    string state = task.Str("state");
                    if (type == "sign")
                    {
                        if (state == "finish") signedByTask = true;
                        int taskStreak = (int)task.Long("sign_in_streak");
                        if (taskStreak > 0) daysByTask = taskStreak;
                    }
                    else if (state != "finish")
                    {
                        string name = task.FirstStr("title", "name", "task_name");
                        if (name.Length > 0) tasks.Add(name);
                    }
                }
            }
            foreach (JsonElement task in list.Arr("list"))
            {
                string name = task.FirstStr("name", "title", "task_name");
                if (name.Length > 0 && !tasks.Contains(name)) tasks.Add(name);
            }
        }

        bool signed = false;
        var signedDates = new HashSet<DateTime>();
        foreach (JsonElement item in result.Arr("sign_list"))
        {
            long stamp = item.Long("date");
            if (stamp <= 0) continue;
            DateTime day = DateTimeOffset.FromUnixTimeSeconds(stamp).LocalDateTime.Date;
            if (!item.Bool("is_sign")) continue;
            signedDates.Add(day);
            if (day == DateTime.Today) signed = true;
        }
        if (!signed)
            signed = signedByTask || result.Bool("signed") || result.Bool("is_sign") || result.Bool("today_signed") ||
                     result.Long("sign_status") == 1;

        int streak = 0;
        DateTime cursor = DateTime.Today;
        if (!signedDates.Contains(cursor)) cursor = cursor.AddDays(-1);
        while (signedDates.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }
        int days = Math.Max(daysByTask,
            (int)result.FirstLong("sign_num", "continuous_sign_num", "sign_days", "days", "sign_in_streak"));
        if (streak > days) days = streak;
        string message = result.FirstStr("msg", "message", "tips", "desc");
        return new XhhSignState
        {
            SignedToday = signed,
            SignDays = days,
            Message = StripHtml(message),
            Tasks = tasks.Take(8).ToList()
        };
    }

    internal static IReadOnlyList<XhhUser> ParseUserList(JsonElement result)
    {
        foreach (string key in new[] { "users", "list", "following_list", "follower_list", "items" })
        {
            IReadOnlyList<JsonElement> array = result.Arr(key);
            if (array.Count == 0) continue;
            var users = new List<XhhUser>();
            foreach (JsonElement element in array)
            {
                JsonElement user = element.Obj("user");
                if (user.ValueKind != JsonValueKind.Object) user = element;
                XhhUser parsed = ParseUser(user);
                if (parsed.Id.Length > 0) users.Add(parsed);
            }
            if (users.Count > 0) return users;
        }
        return [];
    }

    private static List<string> ReadUrls(JsonElement element, string name)
    {
        var urls = new List<string>();
        foreach (JsonElement item in element.Arr(name))
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value)) urls.Add(value);
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                string value = item.FirstStr("url", "src", "image", "thumb", "original");
                if (value.Length > 0) urls.Add(value);
            }
        }
        return Deduplicate(urls);
    }

    private static List<string> Deduplicate(List<string> urls)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return urls.Where(url => !string.IsNullOrWhiteSpace(url) && seen.Add(url)).ToList();
    }

    private static bool IsEmojiOrAvatar(string url)
        => url.Contains("/avatar/", StringComparison.OrdinalIgnoreCase) ||
           url.Contains("/emoji/", StringComparison.OrdinalIgnoreCase) ||
           url.Contains("static.max-c.com/heybox_web/emoji", StringComparison.OrdinalIgnoreCase) ||
           url.Contains("/oa/", StringComparison.OrdinalIgnoreCase) ||
           url.Contains("/game/header/", StringComparison.OrdinalIgnoreCase);
}
