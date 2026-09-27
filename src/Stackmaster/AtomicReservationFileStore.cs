#nullable disable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;

namespace Stackmaster
{
    /// <summary>
    /// Atomic durable storage for reservation payloads. The candidate is flushed to a detached
    /// file before one same-directory rename publishes it, so a failed write leaves either the
    /// complete previous payload or the complete candidate. If an API reports an ambiguous rename
    /// failure, reading the destination resolves whether the candidate actually committed.
    /// </summary>
    internal static class AtomicReservationFileStore
    {
        private const string DirectoryName = "Stackmaster.Reservations";

        internal static bool TryRead(string logicalKey, out string payload, out bool exists)
        {
            payload = string.Empty;
            exists = false;
            var path = PathFor(logicalKey);
            if (!File.Exists(path))
            {
                return true;
            }

            payload = File.ReadAllText(path, Encoding.UTF8);
            exists = true;
            return true;
        }

        internal static bool TryWrite(string logicalKey, string previousPayload, string candidatePayload)
        {
            if (string.IsNullOrEmpty(logicalKey) || previousPayload == null || candidatePayload == null)
            {
                return false;
            }

            var destination = PathFor(logicalKey);
            var directory = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(directory);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                WriteThrough(temporary, candidatePayload);
                try
                {
                    if (File.Exists(destination))
                    {
                        File.Replace(temporary, destination, null);
                    }
                    else
                    {
                        File.Move(temporary, destination);
                    }
                    // A successful same-directory replace/move is the publication commit point.
                    // Do not turn that known success into an ambiguous failure if a subsequent
                    // verification read is denied or transiently unavailable.
                    temporary = null;
                    return true;
                }
                catch
                {
                    // Rename APIs can report an error after the namespace operation completed.
                    // Treat the write as successful only when the exact candidate is now durable;
                    // the old exact payload is an unambiguous failure. Atomic same-directory
                    // replacement prevents a partially written candidate from becoming visible.
                    if (DestinationEquals(destination, candidatePayload))
                    {
                        temporary = null;
                        return true;
                    }
                    if (DestinationEquals(destination, previousPayload))
                    {
                        return false;
                    }
                    throw;
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporary))
                {
                    try
                    {
                        File.Delete(temporary);
                    }
                    catch
                    {
                        // An unpublished temporary file is inert and may be cleaned up later.
                    }
                }
            }
        }

        private static bool DestinationEquals(string path, string expected)
        {
            return File.Exists(path) && string.Equals(
                File.ReadAllText(path, Encoding.UTF8),
                expected,
                StringComparison.Ordinal);
        }

        private static void WriteThrough(string path, string payload)
        {
            var bytes = new UTF8Encoding(false).GetBytes(payload);
            using (var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static string PathFor(string logicalKey)
        {
            if (string.IsNullOrEmpty(logicalKey))
            {
                throw new ArgumentException("A reservation storage key is required.", nameof(logicalKey));
            }

            byte[] digest;
            using (var sha = SHA256.Create())
            {
                digest = sha.ComputeHash(Encoding.UTF8.GetBytes(logicalKey));
            }
            var fileName = BitConverter.ToString(digest).Replace("-", string.Empty).ToLowerInvariant() + ".json";
            return Path.Combine(Paths.ConfigPath, DirectoryName, fileName);
        }
    }
}
