using System;
using System.IO;

namespace SAM.Game
{
    internal static class UserConsent
    {
        private static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KFM Companion");

        private static readonly string FilePath = Path.Combine(DirectoryPath, "agreement.accepted");

        public static bool HasAccepted()
        {
            try
            {
                return File.Exists(FilePath);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static void SaveAccepted()
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                File.WriteAllText(FilePath, DateTime.UtcNow.ToString("o"));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
