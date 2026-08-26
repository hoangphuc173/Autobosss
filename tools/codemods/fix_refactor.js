const fs = require('fs');

const files = [
    'AutoBossGrabber/source/Features/AutoPickupLite.cs',
    'AutoBossGrabber/source/Features/BossSkillManager.cs',
    'AutoBossGrabber/source/Features/MapTransporter.cs',
    'AutoBossGrabber/source/Features/ZoneSwitcher.cs',
    'AutoBossGrabber/source/Features/AutoBossRunner.cs'
];

for (const file of files) {
    if (!fs.existsSync(file)) continue;

    let content = fs.readFileSync(file, 'utf8');

    // Fix the method signatures that were wrongly replaced
    content = content.replace(/[ \t]*private static string UIHelper\.GetButtonText\(Button btn\)[\s\S]*?^    \}/gm, '');
    content = content.replace(/[ \t]*private static string UIHelper\.GetButtonText\(UnityEngine\.UI\.Button btn\)[\s\S]*?^    \}/gm, '');
    content = content.replace(/[ \t]*private static string UIHelper\.GetShortcutText\(Button btn\)[\s\S]*?^    \}/gm, '');
    content = content.replace(/[ \t]*private static string UIHelper\.GetTransformPath\(Transform tr\)[\s\S]*?^    \}/gm, '');

    // Also fix ReflectionHelper explicit interface errors if any
    content = content.replace(/[ \t]*private static object ReflectionHelper\.InvokeNoArg\(object obj, string methodName\)[\s\S]*?^    \}/gm, '');
    content = content.replace(/[ \t]*private static object ReflectionHelper\.GetMemberValue\(object obj, string name\)[\s\S]*?^    \}/gm, '');
    content = content.replace(/[ \t]*private static bool ReflectionHelper\.TryGetIntMember\(object obj, out int value, params string\[\] names\)[\s\S]*?^    \}/gm, '');

    fs.writeFileSync(file, content, 'utf8');
    console.log(`Fixed ${file}`);
}
