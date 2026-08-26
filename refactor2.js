const fs = require('fs');

const files = [
    'AutoBossGrabber/source/Features/AutoPickupLite.cs',
    'AutoBossGrabber/source/Features/BossSkillManager.cs'
];

for (const file of files) {
    if (!fs.existsSync(file)) continue;

    let content = fs.readFileSync(file, 'utf8');

    // Replace method calls
    content = content.replace(/InvokeNoArg\(/g, 'ReflectionHelper.InvokeNoArg(');
    content = content.replace(/GetMemberValue\(/g, 'ReflectionHelper.GetMemberValue(');
    content = content.replace(/TryGetIntMember\(/g, 'ReflectionHelper.TryGetIntMember(');

    // Remove the method definitions
    content = content.replace(/[ \t]*private static object ReflectionHelper\.InvokeNoArg\(object obj, string methodName\)[\s\S]*?^    \}/gm, '');
    content = content.replace(/[ \t]*private static object ReflectionHelper\.GetMemberValue\(object obj, string name\)[\s\S]*?^    \}/gm, '');
    content = content.replace(/[ \t]*private static bool ReflectionHelper\.TryGetIntMember\(object obj, out int value, params string\[\] names\)[\s\S]*?^    \}/gm, '');

    fs.writeFileSync(file, content, 'utf8');
    console.log(`Processed ${file}`);
}
