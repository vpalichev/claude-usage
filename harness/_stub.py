import re, sys
p = r'D:\projects\claude-usage\harness\Panel.fs'
src = open(p, encoding='utf-8').read()
mode = sys.argv[1]
if mode == 'stub':
    pattern = re.compile(r'\\u([0-9A-Fa-f]{4})')
    new = pattern.sub(lambda m: f'ZZZ_U_{m.group(1).upper()}_', src)
elif mode == 'restore':
    pattern = re.compile(r'ZZZ_U_([0-9A-F]{4})_')
    new = pattern.sub(lambda m: f'\\u{m.group(1)}', src)
else:
    sys.exit('bad mode')
open(p, 'w', encoding='utf-8', newline='\n').write(new)
print(mode, 'ok')
