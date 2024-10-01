using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

class Program
{
    static string root = Path.Combine(Directory.GetCurrentDirectory(), "..");

    static void Main(string[] args)
    {
        // Step 1: 解析 dump.cs 并提取出 enums 和 structs
        var (structs, enums) = DumpCsToStructsAndEnums("dump.cs");

        // Step 2: 生成 .fbs 文件
        string fbsPath = GenerateFbs(structs, enums, "BlueArchive.fbs");

        // Step 3: 使用 flatc 生成C#代码
        GenerateCSharpFromFbs(fbsPath);
    }

    static (Dictionary<string, Dictionary<string, string>>, Dictionary<string, EnumData>) DumpCsToStructsAndEnums(string dumpCsFilepath)
    {
        string data = File.ReadAllText(dumpCsFilepath);
        var enums = ExtractEnums(data);
        var structs = ExtractStructs(data);
        return (structs, enums);
    }

    static string GenerateFbs(Dictionary<string, Dictionary<string, string>> structs, Dictionary<string, EnumData> enums, string filepath)
    {
        string fbsPath = Path.Combine(filepath);
        using (var f = new StreamWriter(fbsPath, false, System.Text.Encoding.UTF8))
        {
            f.WriteLine("namespace FlatData;\n");
            WriteEnumsToFbs(enums, f);
            WriteStructsToFbs(structs, enums, f);
        }
        return fbsPath;
    }

    static void GenerateCSharpFromFbs(string fbsPath)
    {
        string flatcPath = Path.Combine("lib", "flatc.exe");

        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = flatcPath,
            Arguments = $"--csharp --scoped-enums {fbsPath}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = System.Diagnostics.Process.Start(processInfo);
        process.WaitForExit();

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();

        Console.WriteLine(output);
        if (!string.IsNullOrEmpty(error))
        {
            Console.WriteLine($"Error: {error}");
        }
    }

    static Dictionary<string, EnumData> ExtractEnums(string data)
    {
        var enums = new Dictionary<string, EnumData>();
        var enumRegex = new Regex(@"// Namespace: FlatData\npublic enum (.{1,128}?) // TypeDefIndex: \d+?\n\{\n(.+?)\}", RegexOptions.Singleline);

        foreach (Match match in enumRegex.Matches(data))
        {
            string enumName = match.Groups[1].Value;
            string enumBody = match.Groups[2].Value;

            var enumFields = new Dictionary<string, string>();
            var fieldRegex = new Regex(@"public const (.+?) (.+?) = (-?\d+?);");

            foreach (Match fieldMatch in fieldRegex.Matches(enumBody))
            {
                enumFields[fieldMatch.Groups[3].Value] = fieldMatch.Groups[2].Value;
            }

            enums[enumName] = new EnumData { Fields = enumFields };
        }
        return enums;
    }

    static Dictionary<string, Dictionary<string, string>> ExtractStructs(string data)
    {
        var structs = new Dictionary<string, Dictionary<string, string>>();
        var structRegex = new Regex(@"struct (.{1,128}?) : .{0,128}?IFlatbufferObject\n\{\n(.+?)\}", RegexOptions.Singleline);

        foreach (Match match in structRegex.Matches(data))
        {
            string structName = match.Groups[1].Value;
            string structBody = match.Groups[2].Value;

            var structFields = new Dictionary<string, string>();
            var propertyRegex = new Regex(@"public (.+) (.+?) { get; }");

            foreach (Match propMatch in propertyRegex.Matches(structBody))
            {
                structFields[propMatch.Groups[2].Value] = propMatch.Groups[1].Value;
            }

            structs[structName] = structFields;
        }
        return structs;
    }

    static void WriteEnumsToFbs(Dictionary<string, EnumData> enums, StreamWriter f)
    {
        foreach (var enumItem in enums)
        {
            string enumName = enumItem.Key;
            var enumData = enumItem.Value;

            f.WriteLine($"enum {enumName} : int {{");
            foreach (var field in enumData.Fields)
            {
                f.WriteLine($"    {field.Value} = {field.Key},");
            }
            f.WriteLine("}\n");
        }
    }

    static void WriteStructsToFbs(Dictionary<string, Dictionary<string, string>> structs, Dictionary<string, EnumData> enums, StreamWriter f)
    {
        foreach (var structItem in structs)
        {
            string structName = structItem.Key;
            var structFields = structItem.Value;

            f.WriteLine($"table {structName} {{");
            foreach (var field in structFields)
            {
                f.WriteLine($"    {field.Key}: {field.Value};");
            }
            f.WriteLine("}\n");
        }
    }

    class EnumData
    {
        public Dictionary<string, string> Fields { get; set; }
    }
}
