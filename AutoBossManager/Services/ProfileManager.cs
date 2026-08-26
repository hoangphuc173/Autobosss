using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using AutoBossShared;

namespace AutoBossManager.Services
{
    /// <summary>
    /// Manages bot profile storage and retrieval.
    /// Handles JSON serialization to AppData folder.
    /// Enhanced with validation, import/export, and automatic backups.
    /// </summary>
    public class ProfileManager : IDisposable
    {
        private readonly string _profilesDirectory;
        private readonly string _backupsDirectory;
        private Timer _backupTimer;

        public ProfileManager()
        {
            // Store profiles in AppData\AutoBossManager\profiles\
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _profilesDirectory = Path.Combine(appDataPath, "AutoBossManager", "profiles");
            _backupsDirectory = Path.Combine(appDataPath, "AutoBossManager", "backups");

            // Create directories if they don't exist
            Directory.CreateDirectory(_profilesDirectory);
            Directory.CreateDirectory(_backupsDirectory);

            // Schedule daily backups (first after 1 hour, then every 24 hours)
            _backupTimer = new Timer(BackupTimerCallback, null,
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(24));
        }

        // === Task 8.1: Basic CRUD Operations ===

        /// <summary>
        /// Save a bot profile to disk with validation
        /// </summary>
        public void SaveProfile(BotProfile profile)
        {
            // Task 8.2: Validate before saving
            var validation = ValidateProfile(profile);
            if (!validation.IsValid)
            {
                throw new ArgumentException("Profile validation failed: " +
                    string.Join(", ", validation.Errors));
            }

            var filePath = GetProfilePath(profile.AccountName);
            var json = JsonConvert.SerializeObject(profile, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Load a bot profile from disk
        /// </summary>
        public BotProfile? LoadProfile(string accountName)
        {
            var filePath = GetProfilePath(accountName);

            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<BotProfile>(json);
        }

        /// <summary>
        /// Load all bot profiles from disk
        /// </summary>
        public List<BotProfile> LoadAllProfiles()
        {
            var profiles = new List<BotProfile>();
            var files = Directory.GetFiles(_profilesDirectory, "*.json");

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var profile = JsonConvert.DeserializeObject<BotProfile>(json);
                    if (profile != null)
                    {
                        profiles.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load profile {file}: {ex.Message}");
                }
            }

            return profiles;
        }

        /// <summary>
        /// Delete a bot profile from disk
        /// </summary>
        public void DeleteProfile(string accountName)
        {
            var filePath = GetProfilePath(accountName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        /// <summary>
        /// Check if a profile exists
        /// </summary>
        public bool ProfileExists(string accountName)
        {
            return File.Exists(GetProfilePath(accountName));
        }

        // === Task 8.2: Profile Validation ===

        /// <summary>
        /// Validate a bot profile against business rules
        /// Requirements: REQ 14.1, 14.2, 14.3, 14.6
        /// </summary>
        public ValidationResult ValidateProfile(BotProfile profile)
        {
            var result = new ValidationResult { IsValid = true };

            // Required fields
            if (string.IsNullOrWhiteSpace(profile.AccountName))
            {
                result.Errors.Add("AccountName is required");
                result.IsValid = false;
            }

            if (string.IsNullOrWhiteSpace(profile.GameExecutablePath))
            {
                result.Errors.Add("GameExecutablePath is required");
                result.IsValid = false;
            }

            if (string.IsNullOrWhiteSpace(profile.Username))
            {
                result.Errors.Add("Username is required");
                result.IsValid = false;
            }

            if (string.IsNullOrWhiteSpace(profile.Password))
            {
                result.Errors.Add("Password is required");
                result.IsValid = false;
            }

            // Minimum safe values (REQ 14.1, 14.2)
            if (profile.RetreatHpPct < 10f || profile.RetreatHpPct > 100f)
            {
                result.Errors.Add("RetreatHpPct must be between 10% and 100%");
                result.IsValid = false;
            }

            if (profile.AttackRange < 0.3f || profile.AttackRange > 10f)
            {
                result.Errors.Add("AttackRange must be between 0.3 and 10");
                result.IsValid = false;
            }

            if (profile.CombatTimeoutSec < 5f || profile.CombatTimeoutSec > 300f)
            {
                result.Errors.Add("CombatTimeoutSec must be between 5 and 300 seconds");
                result.IsValid = false;
            }

            if (profile.LootRadius < 0.5f || profile.LootRadius > 20f)
            {
                result.Errors.Add("LootRadius must be between 0.5 and 20");
                result.IsValid = false;
            }

            // Maximum safe values (REQ 14.3)
            if (profile.MaxZoneAttempts < 1 || profile.MaxZoneAttempts > 50)
            {
                result.Errors.Add("MaxZoneAttempts must be between 1 and 50");
                result.IsValid = false;
            }

            // Boss skill triggers validation (REQ 14.6)
            if (profile.BossSkillTriggers != null && profile.BossSkillTriggers.Count > 0)
            {
                for (int i = 0; i < profile.BossSkillTriggers.Count; i++)
                {
                    var trigger = profile.BossSkillTriggers[i];

                    if (trigger.HpThreshold < 0f || trigger.HpThreshold > 100f)
                    {
                        result.Errors.Add($"Skill trigger #{i + 1}: HpThreshold must be between 0% and 100%");
                        result.IsValid = false;
                    }

                    if (trigger.SpamCount < 1 || trigger.SpamCount > 10)
                    {
                        result.Errors.Add($"Skill trigger #{i + 1}: SpamCount must be between 1 and 10");
                        result.IsValid = false;
                    }
                }
            }

            return result;
        }

        // === Task 8.3: Import/Export Functionality ===

        /// <summary>
        /// Export all profiles to a single JSON file
        /// Requirements: REQ 18.1, 18.2
        /// </summary>
        public void ExportProfiles(string exportFilePath)
        {
            var allProfiles = LoadAllProfiles();
            var export = new BulkProfilesExport
            {
                Version = "1.0",
                ExportDate = DateTime.UtcNow,
                Profiles = allProfiles
            };

            var json = JsonConvert.SerializeObject(export, Formatting.Indented);
            File.WriteAllText(exportFilePath, json);

            Console.WriteLine($"[ProfileManager] Exported {allProfiles.Count} profiles to {exportFilePath}");
        }

        /// <summary>
        /// Import profiles from a JSON file
        /// Requirements: REQ 18.3, 18.7
        /// </summary>
        public ImportResult ImportProfiles(string importFilePath,
            DuplicateHandling duplicateHandling = DuplicateHandling.Skip)
        {
            var result = new ImportResult();

            if (!File.Exists(importFilePath))
            {
                result.Errors.Add($"Import file not found: {importFilePath}");
                return result;
            }

            try
            {
                var json = File.ReadAllText(importFilePath);
                var import = JsonConvert.DeserializeObject<BulkProfilesExport>(json);

                if (import == null || import.Profiles == null)
                {
                    result.Errors.Add("Invalid import file format");
                    return result;
                }

                Console.WriteLine($"[ProfileManager] Importing {import.Profiles.Count} profiles from {importFilePath}");

                foreach (var profile in import.Profiles)
                {
                    try
                    {
                        // Validate profile
                        var validation = ValidateProfile(profile);
                        if (!validation.IsValid)
                        {
                            result.SkippedProfiles.Add(profile.AccountName ?? "Unknown");
                            result.Errors.Add($"{profile.AccountName}: {string.Join(", ", validation.Errors)}");
                            continue;
                        }

                        // Handle duplicates
                        if (ProfileExists(profile.AccountName))
                        {
                            switch (duplicateHandling)
                            {
                                case DuplicateHandling.Overwrite:
                                    SaveProfile(profile);
                                    result.OverwrittenProfiles.Add(profile.AccountName);
                                    Console.WriteLine($"[ProfileManager] Overwritten: {profile.AccountName}");
                                    break;

                                case DuplicateHandling.Skip:
                                    result.SkippedProfiles.Add(profile.AccountName);
                                    Console.WriteLine($"[ProfileManager] Skipped (duplicate): {profile.AccountName}");
                                    break;

                                case DuplicateHandling.Rename:
                                    string originalName = profile.AccountName;
                                    profile.AccountName = GetUniqueName(profile.AccountName);
                                    SaveProfile(profile);
                                    result.ImportedProfiles.Add(profile.AccountName);
                                    Console.WriteLine($"[ProfileManager] Renamed {originalName} ? {profile.AccountName}");
                                    break;
                            }
                        }
                        else
                        {
                            SaveProfile(profile);
                            result.ImportedProfiles.Add(profile.AccountName);
                            Console.WriteLine($"[ProfileManager] Imported: {profile.AccountName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"{profile.AccountName ?? "Unknown"}: {ex.Message}");
                        result.SkippedProfiles.Add(profile.AccountName ?? "Unknown");
                    }
                }

                Console.WriteLine($"[ProfileManager] Import complete: {result.ImportedProfiles.Count} imported, " +
                    $"{result.OverwrittenProfiles.Count} overwritten, {result.SkippedProfiles.Count} skipped");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Import failed: {ex.Message}");
            }

            return result;
        }

        // === Task 8.4: Automatic Backup ===

        private void BackupTimerCallback(object? state)
        {
            try
            {
                BackupProfiles();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProfileManager] Backup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Create a timestamped backup of all profiles
        /// Requirements: REQ 18.5
        /// </summary>
        public void BackupProfiles()
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = $"profiles_backup_{timestamp}.json";
                var backupFilePath = Path.Combine(_backupsDirectory, backupFileName);

                // Export all profiles to backup file
                ExportProfiles(backupFilePath);

                Console.WriteLine($"[ProfileManager] Backup created: {backupFileName}");

                // Clean up old backups (retain last 7)
                CleanupOldBackups();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProfileManager] Backup error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Delete old backups, retaining only the last 7
        /// Requirements: REQ 18.6
        /// </summary>
        private void CleanupOldBackups()
        {
            try
            {
                var backupFiles = Directory.GetFiles(_backupsDirectory, "profiles_backup_*.json")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                // Keep last 7 backups, delete older ones
                var filesToDelete = backupFiles.Skip(7);
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        file.Delete();
                        Console.WriteLine($"[ProfileManager] Deleted old backup: {file.Name}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ProfileManager] Failed to delete {file.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProfileManager] Cleanup error: {ex.Message}");
            }
        }

        // === Helper Methods ===

        /// <summary>
        /// Get the file path for a profile
        /// </summary>
        private string GetProfilePath(string accountName)
        {
            var safeFileName = string.Join("_", accountName.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(_profilesDirectory, $"{safeFileName}.json");
        }

        /// <summary>
        /// Generate a unique profile name by appending a counter
        /// </summary>
        private string GetUniqueName(string baseName)
        {
            int counter = 1;
            string newName = baseName;

            while (ProfileExists(newName))
            {
                newName = $"{baseName}_{counter}";
                counter++;
            }

            return newName;
        }

        // === IDisposable ===

        public void Dispose()
        {
            _backupTimer?.Dispose();
        }
    }

    // === Supporting Classes ===

    /// <summary>
    /// Result of profile validation
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Container for bulk profile export
    /// </summary>
    public class BulkProfilesExport
    {
        public string Version { get; set; } = "1.0";
        public DateTime ExportDate { get; set; }
        public List<BotProfile> Profiles { get; set; } = new List<BotProfile>();
    }

    /// <summary>
    /// Result of profile import operation
    /// </summary>
    public class ImportResult
    {
        public List<string> ImportedProfiles { get; set; } = new List<string>();
        public List<string> OverwrittenProfiles { get; set; } = new List<string>();
        public List<string> SkippedProfiles { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();

        public bool HasErrors => Errors.Count > 0;
        public int TotalProcessed => ImportedProfiles.Count + OverwrittenProfiles.Count + SkippedProfiles.Count;
    }

    /// <summary>
    /// How to handle duplicate profile names during import
    /// </summary>
    public enum DuplicateHandling
    {
        Skip,       // Skip importing duplicates
        Overwrite,  // Overwrite existing profiles
        Rename      // Rename imported profiles with _1, _2, etc.
    }
}
