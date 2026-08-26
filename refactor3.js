const fs = require('fs');

const file = 'AutoBossGrabber/source/Core/GameAPI.cs';
let content = fs.readFileSync(file, 'utf8');

// Remove the method definition
content = content.replace(/[ \t]*private static bool ReflectionHelper\.TryGetIntMember\(object obj, out int value, params string\[\] names\)[\s\S]*?^    \}/gm, '');

// Also remove GetTransformPath / GetButtonTextInternal / GetTransformPathSafe from GameAPI.cs
content = content.replace(/GetTransformPathOf\(/g, 'UIHelper.GetTransformPath(');
content = content.replace(/GetTransformPath\(/g, 'UIHelper.GetTransformPath(');
content = content.replace(/GetTransformPathSafe\(/g, 'UIHelper.GetTransformPath(');
content = content.replace(/GetButtonTextInternal\(/g, 'UIHelper.GetButtonText(');

content = content.replace(/[ \t]*private static string UIHelper\.GetTransformPath\(Transform tr\)[\s\S]*?^    \}/gm, '');
content = content.replace(/[ \t]*private static string UIHelper\.GetTransformPathOf\(Transform tr\)[\s\S]*?^    \}/gm, '');
content = content.replace(/[ \t]*private static string UIHelper\.GetTransformPathSafe\(Transform tr\)[\s\S]*?^    \}/gm, '');
content = content.replace(/[ \t]*private static string UIHelper\.GetButtonTextInternal\(UnityEngine\.UI\.Button btn\)[\s\S]*?^    \}/gm, '');

fs.writeFileSync(file, content, 'utf8');
console.log(`Fixed ${file}`);
