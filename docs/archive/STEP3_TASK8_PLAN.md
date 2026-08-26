# 📋 STEP 3: COMPLETE TASK 8 - ProfileManager Enhancements

## Current Status: 40% Complete

### ✅ Already Implemented (Task 8.1):
- Basic CRUD operations (Save, Load, Delete, LoadAll)
- AppData storage path
- JSON serialization via Newtonsoft.Json

### ❌ Missing (Tasks 8.2-8.4):
- Profile validation with rules
- Import/Export functionality
- Automatic backup system

---

## Task 8.2: Profile Validation (HIGH PRIORITY)

### Requirements:
- Validate required fields
- Enforce minimum safe values
- Enforce maximum safe values
- Return validation error messages

### Implementation Plan:

**Add to ProfileManager.cs:**

```csharp
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new List<string>();
}

public ValidationResult ValidateProfile(BotProfile profile)
{
    var result = new ValidationResult { IsValid = true };

    // Required fields
    if (string.IsNullOrEmpty(profile.AccountName))
    {
        result.Errors.Add("AccountName is required");
        result.IsValid = false;
    }

    if (string.IsNullOrEmpty(profile.GameExecutablePath))
    {
        result.Errors.Add("GameExecutablePath is required");
        result.IsValid = false;
    }

    if (string.IsNullOrEmpty(profile.Username))
    {
        result.Errors.Add("Username is required");
        result.IsValid = false;
    }

    if (string.IsNullOrEmpty(profile.Password))
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

    // Maximum safe values (REQ 14.3)
    if (profile.MaxZoneAttempts < 1 || profile.MaxZoneAttempts > 50)
    {
        result.Errors.Add("MaxZoneAttempts must be between 1 and 50");
        result.IsValid = false;
    }

    if (profile.SessionDurationMin < 10 || profile.SessionDurationMin > 720) // Max 12 hours
    {
        result.Errors.Add("SessionDurationMin must be between 10 and 720 minutes (12 hours)");
        result.IsValid = false;
    }

    // Boss skill triggers validation (REQ 14.6)
    if (profile.BossSkillTriggers != null)
    {
        foreach (var trigger in profile.BossSkillTriggers)
        {
            if (trigger.HpThreshold < 0f || trigger.HpThreshold > 100f)
            {
                result.Errors.Add($"Skill trigger HpThreshold must be between 0% and 100%");
                result.IsValid = false;
            }
        }
    }

    return result;
}

// Update SaveProfile to validate first:
public void SaveProfile(BotProfile profile)
{
    var validation = ValidateProfile(profile);
    if (!validation.IsValid)
    {
        throw new ArgumentException("Profile validation failed: " + 
            string.Join(", ", validation.Errors));
    }

    // ... existing save logic ...
}
```

**Estimated Time:** 30-45 minutes

---

## Task 8.3: Import/Export Functionality (MEDIUM PRIORITY)

### Requirements:
- Export all profiles to single JSON file
- Import profiles from JSON file
- Handle duplicate names
- Validate imported profiles

### Implementation Plan:

**Add to ProfileManager.cs:**

```csharp
public class BulkProfilesExport
{
    public string Version { get; set; } = "1.0";
    public DateTime ExportDate { get; set; }
    public List<BotProfile> Profiles { get; set; } = new List<BotProfile>();
}

public void ExportProfiles(string exportFilePath)
{
    var allProfiles = LoadAllProfiles();
    var export = new BulkProfilesExport
    {
        ExportDate = DateTime.UtcNow,
        Profiles = allProfiles
    };

    var json = JsonConvert.SerializeObject(export, Formatting.Indented);
    File.WriteAllText(exportFilePath, json);
    
    Console.WriteLine($"Exported {allProfiles.Count} profiles to {exportFilePath}");
}

public ImportResult ImportProfiles(string importFilePath, 
    DuplicateHandling duplicateHandling = DuplicateHandling.Skip)
{
    var result = new ImportResult();
    
    var json = File.ReadAllText(importFilePath);
    var import = JsonConvert.DeserializeObject<BulkProfilesExport>(json);
    
    if (import == null || import.Profiles == null)
    {
        result.Errors.Add("Invalid import file format");
        return result;
    }

    foreach (var profile in import.Profiles)
    {
        try
        {
            // Validate profile
            var validation = ValidateProfile(profile);
            if (!validation.IsValid)
            {
                result.SkippedProfiles.Add(profile.AccountName);
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
                        break;
                    case DuplicateHandling.Skip:
                        result.SkippedProfiles.Add(profile.AccountName);
                        break;
                    case DuplicateHandling.Rename:
                        profile.AccountName = GetUniqueName(profile.AccountName);
                        SaveProfile(profile);
                        result.ImportedProfiles.Add(profile.AccountName);
                        break;
                }
            }
            else
            {
                SaveProfile(profile);
                result.ImportedProfiles.Add(profile.AccountName);
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"{profile.AccountName}: {ex.Message}");
            result.SkippedProfiles.Add(profile.AccountName);
        }
    }

    return result;
}

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

public enum DuplicateHandling
{
    Skip,
    Overwrite,
    Rename
}

public class ImportResult
{
    public List<string> ImportedProfiles { get; set; } = new List<string>();
    public List<string> OverwrittenProfiles { get; set; } = new List<string>();
    public List<string> SkippedProfiles { get; set; } = new List<string>();
    public List<string> Errors { get; set; } = new List<string>();
}
```

**Estimated Time:** 45-60 minutes

---

## Task 8.4: Automatic Backup (LOW PRIORITY)

### Requirements:
- Daily backups
- Retain last 7 backups
- Timestamped backup files
- Background thread execution

### Implementation Plan:

**Add to ProfileManager.cs:**

```csharp
private readonly string _backupsDirectory;
private Timer _backupTimer;

public ProfileManager()
{
    // ... existing initialization ...
    
    _backupsDirectory = Path.Combine(appDataPath, "AutoBossManager", "backups");
    Directory.CreateDirectory(_backupsDirectory);
    
    // Schedule daily backups (every 24 hours)
    _backupTimer = new Timer(BackupTimerCallback, null, 
        TimeSpan.FromHours(1), // First backup after 1 hour
        TimeSpan.FromHours(24)); // Then every 24 hours
}

private void BackupTimerCallback(object state)
{
    try
    {
        BackupProfiles();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Backup failed: {ex.Message}");
    }
}

public void BackupProfiles()
{
    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var backupFileName = $"profiles_backup_{timestamp}.json";
    var backupFilePath = Path.Combine(_backupsDirectory, backupFileName);
    
    // Export all profiles to backup file
    ExportProfiles(backupFilePath);
    
    Console.WriteLine($"Backup created: {backupFileName}");
    
    // Clean up old backups (retain last 7)
    CleanupOldBackups();
}

private void CleanupOldBackups()
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
            Console.WriteLine($"Deleted old backup: {file.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete {file.Name}: {ex.Message}");
        }
    }
}

// Don't forget IDisposable
public void Dispose()
{
    _backupTimer?.Dispose();
}
```

**Estimated Time:** 30-45 minutes

---

## Total Estimated Time: 2-3 hours

### Breakdown:
- Task 8.2 (Validation): 30-45 minutes
- Task 8.3 (Import/Export): 45-60 minutes
- Task 8.4 (Backup): 30-45 minutes
- Testing: 30 minutes

---

## Implementation Order (Recommended):

1. **Task 8.2 first** (validation) - Most critical
2. **Task 8.3 second** (import/export) - User-facing feature
3. **Task 8.4 last** (backup) - Background automation

---

## Testing Plan:

### Test 8.2: Validation
```csharp
// Test invalid profile
var profile = new BotProfile { AccountName = "" }; // Missing required field
var result = profileManager.ValidateProfile(profile);
Assert.False(result.IsValid);
Assert.Contains("AccountName is required", result.Errors);

// Test out-of-range values
profile.RetreatHpPct = 5f; // Below minimum
result = profileManager.ValidateProfile(profile);
Assert.False(result.IsValid);
```

### Test 8.3: Import/Export
```csharp
// Export all profiles
profileManager.ExportProfiles("test_export.json");
Assert.True(File.Exists("test_export.json"));

// Import with skip duplicates
var result = profileManager.ImportProfiles("test_export.json", 
    DuplicateHandling.Skip);
Assert.NotEmpty(result.SkippedProfiles); // Duplicates skipped
```

### Test 8.4: Backup
```csharp
// Trigger manual backup
profileManager.BackupProfiles();

// Check backup file exists
var backups = Directory.GetFiles(backupsDirectory, "profiles_backup_*.json");
Assert.NotEmpty(backups);

// Create 10 backups, verify cleanup keeps only 7
for (int i = 0; i < 10; i++)
{
    profileManager.BackupProfiles();
    Thread.Sleep(1000); // Different timestamps
}
backups = Directory.GetFiles(backupsDirectory, "profiles_backup_*.json");
Assert.Equal(7, backups.Length);
```

---

## Next Step After Task 8:

Once Task 8 complete:
- Update PROGRESS_SUMMARY.md (Task 8: 100% ✅)
- Move to Task 9 enhancements (filtering, sorting)
- Or move to Task 11 (BFSPathfinder) if Phase 2 complete

---

**Ready to Implement?**
I can execute all 3 subtasks (8.2-8.4) now if you want!
