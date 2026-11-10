using System.Text.Json.Nodes;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class ProviderServer
{
    private static void ValidateDesktopActions(JsonNode? node)
    {
        if (node is not JsonArray actions)
        {
            throw InvalidRequest("desktop.actions must be an array.");
        }
        if (actions.Count == 0)
        {
            throw InvalidRequest("Provider desktop actions are required.");
        }
        for (var index = 0; index < actions.Count; index++)
        {
            var path = $"desktop.actions[{index}]";
            var action = RequiredObject(actions[index], path);
            RejectUnknownFields(
                action,
                Fields(
                    "type", "x", "y", "button", "text", "keys", "path",
                    "scroll_x", "scroll_y", "region"),
                path);
            var type = RequiredStringValue(action, "type");
            ValidateDesktopAction(action, type, path);
            if (action["path"] is not JsonArray points)
            {
                continue;
            }
            for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                ValidateRequiredObject(
                    points[pointIndex],
                    $"{path}.path[{pointIndex}]",
                    Fields("x", "y"));
            }
        }
    }

    private static void ValidateDesktopAction(
        JsonObject action,
        string type,
        string path)
    {
        var hasX = action["x"] is not null;
        var hasY = action["y"] is not null;
        var hasPoint = hasX || hasY;
        var hasButton = action["button"] is not null;
        var hasText = action["text"] is not null;
        var hasKeys = action["keys"] is not null;
        var hasPath = action["path"] is not null;
        var hasScrollX = action["scroll_x"] is not null;
        var hasScrollY = action["scroll_y"] is not null;
        if (hasX)
        {
            RequiredNumber(action, "x", path);
        }
        if (hasY)
        {
            RequiredNumber(action, "y", path);
        }
        if (hasButton)
        {
            RequireString(action, "button", path, allowEmpty: true);
        }
        if (hasText)
        {
            RequireString(action, "text", path, allowEmpty: true);
        }
        ValidateOptionalStringArray(action, "keys", path);
        if (hasScrollX)
        {
            RequiredNumber(action, "scroll_x", path);
        }
        if (hasScrollY)
        {
            RequiredNumber(action, "scroll_y", path);
        }

        var hasRegion = action["region"] is not null;
        if (hasRegion && type != "zoom")
        {
            throw InvalidRequest($"{path}.{type} does not accept region.");
        }
        switch (type)
        {
            case "zoom":
                if (action["region"] is not JsonArray region ||
                    region.Count != 4 ||
                    region.Any(value =>
                        value is not JsonValue number ||
                        !number.TryGetValue<double>(out _)))
                {
                    throw InvalidRequest($"{path}.region must be four numbers.");
                }
                RejectDesktopFields(
                    hasPoint || hasButton || hasText || hasKeys || hasPath ||
                    hasScrollX || hasScrollY,
                    "zoom",
                    path);
                break;
            case "screenshot":
                RejectDesktopFields(
                    hasPoint || hasButton || hasText || hasKeys || hasPath ||
                    hasScrollX || hasScrollY,
                    "screenshot",
                    path);
                break;
            case "click":
                RequireDesktopPoint(hasX, hasY, "click", path);
                var button = StringValue(action, "button") ?? string.Empty;
                if (button.Length > 0 &&
                    button is not "left" and not "right" and not "wheel" and
                        not "back" and not "forward")
                {
                    throw InvalidRequest(
                        $"{path}.button has unsupported value {button}.");
                }
                RejectDesktopFields(
                    hasText || hasPath || hasScrollX || hasScrollY,
                    "click",
                    path);
                break;
            case "double_click":
            case "move":
                RequireDesktopPoint(hasX, hasY, type, path);
                RejectDesktopFields(
                    hasButton || hasText || hasPath || hasScrollX || hasScrollY,
                    type,
                    path);
                break;
            case "drag":
                if (action["path"] is not JsonArray dragPath ||
                    dragPath.Count < 2)
                {
                    throw InvalidRequest(
                        $"{path}.path must contain at least two points.");
                }
                RejectDesktopFields(
                    hasPoint || hasButton || hasText || hasScrollX || hasScrollY,
                    "drag",
                    path);
                break;
            case "scroll":
                RequireDesktopPoint(hasX, hasY, "scroll", path);
                var scrollX = OptionalNumber(action, "scroll_x") ?? 0;
                var scrollY = OptionalNumber(action, "scroll_y") ?? 0;
                if (scrollX == 0 && scrollY == 0)
                {
                    throw InvalidRequest(
                        $"{path} requires a non-zero scroll_x or scroll_y.");
                }
                RejectDesktopFields(
                    hasButton || hasText || hasPath,
                    "scroll",
                    path);
                break;
            case "type":
                RequireNonEmptyString(action, "text", path);
                RejectDesktopFields(
                    hasPoint || hasButton || hasKeys || hasPath ||
                    hasScrollX || hasScrollY,
                    "type",
                    path);
                break;
            case "keypress":
                if (action["keys"] is not JsonArray keys || keys.Count == 0)
                {
                    throw InvalidRequest($"{path}.keys must be non-empty.");
                }
                for (var index = 0; index < keys.Count; index++)
                {
                    if (keys[index] is not JsonValue key ||
                        !key.TryGetValue<string>(out var text) ||
                        string.IsNullOrWhiteSpace(text))
                    {
                        throw InvalidRequest(
                            $"{path}.keys[{index}] must be a non-empty string.");
                    }
                }
                RejectDesktopFields(
                    hasPoint || hasButton || hasText || hasPath ||
                    hasScrollX || hasScrollY,
                    "keypress",
                    path);
                break;
            case "wait":
                RejectDesktopFields(
                    hasPoint || hasButton || hasText || hasKeys || hasPath ||
                    hasScrollX || hasScrollY,
                    "wait",
                    path);
                break;
            default:
                throw InvalidRequest(
                    $"{path}.type has unsupported value {type}.");
        }
    }

    private static void RequireDesktopPoint(
        bool hasX,
        bool hasY,
        string type,
        string path)
    {
        if (!hasX || !hasY)
        {
            throw InvalidRequest($"{path} {type} requires x and y.");
        }
    }

    private static void RejectDesktopFields(
        bool rejected,
        string type,
        string path)
    {
        if (rejected)
        {
            throw InvalidRequest(
                $"{path} {type} contains fields for another action.");
        }
    }
}
