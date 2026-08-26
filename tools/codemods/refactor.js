const fs = require('fs');
const path = require('path');

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

    // Replace method calls
    content = content.replace(/GetButtonText\(/g, 'UIHelper.GetButtonText(');
    content = content.replace(/GetShortcutText\(/g, 'UIHelper.GetShortcutText(');
    content = content.replace(/GetTransformPath\(/g, 'UIHelper.GetTransformPath(');

    // AutoBossRunner uses UnityEngine.UI.Button instead of Button
    content = content.replace(/UIHelper\.GetButtonText\(\(UnityEngine\.UI\.Button\)/g, 'UIHelper.GetButtonText(');
    content = content.replace(/private static string UIHelper\.GetButtonText\(UnityEngine\.UI\.Button btn\)/g, 'private static string GetButtonText(UnityEngine.UI.Button btn)');

    // Remove the method definitions using regex
    content = content.replace(/[ \t]*private static string GetButtonText\(Button btn\)[\s\S]*?^    \}/gm, '');
    content = content.replace(/[ \t]*private static string GetButtonText\(UnityEngine\.UI\.Button btn\)[\s\S]*?^    \}/gm, '');

    content = content.replace(/[ \t]*private static string GetShortcutText\(Button btn\)[\s\S]*?^    \}/gm, '');
    content = content.replace(/[ \t]*private static string GetTransformPath\(Transform tr\)[\s\S]*?^    \}/gm, '');

    fs.writeFileSync(file, content, 'utf8');
    console.log(`Processed ${file}`);
}
