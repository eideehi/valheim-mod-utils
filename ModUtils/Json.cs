using System.Text;
using LitJson;

namespace ModUtils
{
    public static class Json
    {
        public static void AddImporter<TJson, TValue>(ImporterFunc<TJson, TValue> importer)
        {
            JsonMapper.RegisterImporter(importer);
        }

        public static void AddExporter<T>(ExporterFunc<T> exporter)
        {
            JsonMapper.RegisterExporter(exporter);
        }

        public static T Parse<T>(string jsonText, ReaderOption option)
        {
            return JsonMapper.ToObject<T>(new JsonReader(jsonText)
            {
                AllowComments = option.AllowComments,
                AllowSingleQuotedStrings = option.AllowSingleQuotedStrings,
                SkipNonMembers = option.SkipNonMembers
            });
        }

        public static T Parse<T>(string jsonText)
        {
            return Parse<T>(jsonText, new ReaderOption
            {
                AllowComments = true,
                AllowSingleQuotedStrings = false,
                SkipNonMembers = true
            });
        }

        public static string ToString(object obj, WriterOption option)
        {
            var stringBuilder = new StringBuilder();
            JsonMapper.ToJson(obj, new JsonWriter(stringBuilder)
            {
                IndentValue = option.IndentSize,
                PrettyPrint = option.Pretty
            });
            return stringBuilder.ToString();
        }

        public static string ToString(object obj)
        {
            return ToString(obj, new WriterOption
            {
                Pretty = false,
                IndentSize = 0
            });
        }
    }

    public struct ReaderOption
    {
        public bool AllowComments;
        public bool AllowSingleQuotedStrings;
        public bool SkipNonMembers;
    }

    public struct WriterOption
    {
        public bool Pretty;
        public int IndentSize;
    }
}