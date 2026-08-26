const fs = require('fs');

const file = 'AutoBossGrabber/source/Core/GameAPI.cs';
let content = fs.readFileSync(file, 'utf8');

// Remove the method definitions entirely
content = content.replace(/[ \t]*private static string UIHelper\.GetButtonText\(UnityEngine\.UI\.Button btn\)[\s\S]*?^    \}/gm, '');
content = content.replace(/[ \t]*private static string UIHelper\.UIHelper\.GetTransformPath\(Transform tr\)[\s\S]*?^    \}/gm, '');

fs.writeFileSync(file, content, 'utf8');
console.log(`Fixed ${file}`);
