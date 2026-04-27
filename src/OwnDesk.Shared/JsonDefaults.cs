using System.Text.Json;

namespace OwnDesk.Shared;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}

