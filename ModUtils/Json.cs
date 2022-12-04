using LitJson;

namespace ModUtils
{
    public static class Json
    {
        public static T Parse<T>(string jsonText, ParserOption option)
        {
            var reader = new JsonReader(jsonText)
            {
                AllowComments = option.AllowComments,
                AllowSingleQuotedStrings = option.AllowSingleQuotedStrings,
                SkipNonMembers = option.SkipNonMembers
            };
            return JsonMapper.ToObject<T>(reader);
        }

        public static T Parse<T>(string jsonText)
        {
            return Parse<T>(jsonText, new ParserOption
            {
                AllowComments = true,
                AllowSingleQuotedStrings = false,
                SkipNonMembers = true
            });
        }
    }

    public struct ParserOption
    {
        public bool AllowComments;
        public bool AllowSingleQuotedStrings;
        public bool SkipNonMembers;
    }
}