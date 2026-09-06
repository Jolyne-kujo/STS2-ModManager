using Sts2ModManager.Core;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Sts2PckBuilder <source-directory> <output.pck>");
    return 2;
}

var sourceDirectory = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
if (!Directory.Exists(sourceDirectory))
{
    Console.Error.WriteLine($"PCK source directory does not exist: {sourceDirectory}");
    return 2;
}

var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
    .ToDictionary(
        path => Path.GetRelativePath(sourceDirectory, path),
        path => File.ReadAllBytes(path),
        StringComparer.OrdinalIgnoreCase);

GodotPckWriter.Write(outputPath, files);
Console.WriteLine($"Wrote {files.Count} files to {outputPath}");
return 0;
