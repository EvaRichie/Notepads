﻿
namespace Notepads.Utilities
{
    using Microsoft.Toolkit.Uwp.Helpers;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Windows.Storage;

    public class TextFile
    {
        public TextFile(string content, Encoding encoding, LineEnding lineEnding)
        {
            Content = content;
            Encoding = encoding;
            LineEnding = lineEnding;
        }

        public string Content { get; }
        public Encoding Encoding { get; }
        public LineEnding LineEnding { get; }
    }

    public class FileSystemUtility
    {
        public static bool IsFullPath(string path)
        {
            return !String.IsNullOrWhiteSpace(path)
                   && path.IndexOfAny(System.IO.Path.GetInvalidPathChars().ToArray()) == -1
                   && Path.IsPathRooted(path)
                   && !Path.GetPathRoot(path).Equals(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal);
        }

        public static String GetAbsolutePath(String basePath, String path)
        {
            String finalPath;
            if (!Path.IsPathRooted(path) || "\\".Equals(Path.GetPathRoot(path)))
            {
                if (path.StartsWith(Path.DirectorySeparatorChar.ToString()))
                    finalPath = Path.Combine(Path.GetPathRoot(basePath), path.TrimStart(Path.DirectorySeparatorChar));
                else
                    finalPath = Path.Combine(basePath, path);
            }
            else
                finalPath = path;
            // resolves any internal "..\" to get the true full path.
            return Path.GetFullPath(finalPath);
        }

        public static async Task<StorageFile> OpenFileFromCommandLine(string dir, string args)
        {
            var path = GetAbsolutePathFromCommondLine(dir, args);

            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            try
            {
                return await StorageFile.GetFileFromPathAsync(path);
            }
            catch (Exception)
            {
                // ignore
            }

            return null;
        }

        public static string GetAbsolutePathFromCommondLine(string dir, string args)
        {
            if (string.IsNullOrEmpty(args)) return null;

            string path = args;

            if (path.StartsWith("\"") && path.EndsWith("\"") && path.Length > 2)
            {
                path = path.Substring(1, args.Length - 2);
            }

            if (FileSystemUtility.IsFullPath(path))
            {
                return path;
            }

            if (path.StartsWith(".\\"))
            {
                path = dir + Path.DirectorySeparatorChar + path.Substring(2, path.Length - 2);
            }
            else if (path.StartsWith("..\\"))
            {
                path = FileSystemUtility.GetAbsolutePath(dir, path);
            }
            else
            {
                path = dir + Path.DirectorySeparatorChar + path;
            }

            return path;
        }

        public static bool IsFileReadOnly(StorageFile file)
        {
            return (file.Attributes & Windows.Storage.FileAttributes.ReadOnly) != 0;
        }

        public static async Task<bool> FileIsWritable(StorageFile file)
        {
            try
            {
                using (var stream = await file.OpenStreamForWriteAsync()) { }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static async Task<TextFile> ReadFile(StorageFile file)
        {
            var fileProperties = await file.GetBasicPropertiesAsync();

            if (fileProperties.Size > 1000 * 1024) // 1MB
            {
                throw new Exception("Notepads does not support file greater than 1MB at this moment");
            }

            string text;
            Encoding encoding;

            using (var inputStream = await file.OpenReadAsync())
            using (var classicStream = inputStream.AsStreamForRead())
            using (var streamReader = new StreamReader(classicStream))
            {
                streamReader.Peek();
                encoding = streamReader.CurrentEncoding;
                text = streamReader.ReadToEnd();
            }

            return new TextFile(text, encoding, LineEndingUtility.GetLineEndingTypeFromText(text));
        }
    }
}
