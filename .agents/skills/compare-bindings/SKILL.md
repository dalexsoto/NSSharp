---
name: compare-bindings
description: Compare two C# binding definition files (e.g., NSSharp output vs Objective Sharpie output) to identify gaps in attribute coverage, missing exports, naming differences, parameter type mismatches, and return type differences. Use when evaluating binding quality, finding regressions, or measuring improvement after changes.
---

# Compare C# Binding Definitions

Compare two Xamarin.iOS / .NET for iOS C# API definition files to measure binding quality and identify gaps.

## When to Use

- After generating bindings with NSSharp, compare against a reference (e.g., Objective Sharpie output)
- After making changes to the binding generator, measure improvement or detect regressions
- To identify specific categories of differences (naming, types, attributes, missing APIs)

## How to Compare

### Step 1: Generate Fresh Bindings

```bash
cd /Users/alex/xamarin-ios/NSSharp
dotnet run --project src/NSSharp -- --xcframework path/to/MyLib.xcframework --extern-macros MY_EXPORT -o /tmp/nssharp_output.cs
```

### Step 2: Run the Comparison Script

Write and run a Python comparison script. The script should analyze these dimensions:

### Dimension 1: Attribute Counts

Count occurrences of key binding attributes in both files and compare:

```python
import re

def count_attributes(filepath):
    with open(filepath) as f:
        content = f.read()
    patterns = {
        'Export': r'Export\s*\(',
        'BaseType': r'BaseType\s*\(',
        'Protocol': r'\[Protocol\b',
        'Model': r'\[Model\b',
        'Abstract': r'\[Abstract\]',
        'Static': r'\[Static\]',
        'NullAllowed': r'NullAllowed',
        'Field': r'Field\s*\(',
        'DllImport': r'DllImport\s*\(',
        'Wrap': r'Wrap\s*\(',
        'Async': r'\[Async\]',
        'Notification': r'\[Notification\b',
        'Constructor': r'NativeHandle Constructor',
        'DesignatedInitializer': r'\[DesignatedInitializer\]',
        'Category': r'\[Category\]',
        'New': r'\[New\]',
    }
    return {name: len(re.findall(pattern, content)) for name, pattern in patterns.items()}
```

**Key metrics to watch:**
- `Export` should be ≥98% of reference
- `Abstract` should be ~100%
- `Constructor` should be ~96%
- `NullAllowed` should be ~100-105%

### Dimension 2: Missing/Extra Exports

Extract all `[Export("selector")]` selectors and set-diff:

```python
def extract_exports(filepath):
    with open(filepath) as f:
        content = f.read()
    return set(re.findall(r'Export\s*\("([^"]+)"', content))

ref_exports = extract_exports(reference_file)
our_exports = extract_exports(our_file)
missing = ref_exports - our_exports  # in reference but not ours
extra = our_exports - ref_exports    # in ours but not reference
```

### Dimension 3: Method Naming Match Rate

For each shared `[Export]` selector, extract the C# method name and compare:

```python
def get_export_to_name(text):
    lines = text.split('\n')
    results = {}
    for i, line in enumerate(lines):
        m = re.search(r'Export\s*\("([^"]+)"', line)
        if m:
            sel = m.group(1)
            for j in range(i+1, min(i+5, len(lines))):
                ns = lines[j].strip()
                if not ns.startswith('[') and not ns.startswith('//') and ns:
                    parts = ns.split('(', 1)
                    name_part = parts[0].strip()
                    name = name_part.split()[-1] if name_part else ''
                    results[sel] = name
                    break
    return results
```

**Target: ≥89% match rate.** Common diff categories:
- Parse errors (one side has `}` as name) — protocol property style differences
- Extra/missing `Get` prefix
- Casing differences (sharpie quirks like `handleExternalUrl`)
- Preposition context differences

### Dimension 4: Return Type Differences

For each shared export, compare the return type (everything before the method name):

```python
def extract_return_type(decl):
    if '(' in decl:
        return decl.split('(')[0].strip().rsplit(' ', 1)[0]
    return ''
```

**Common return type diff categories:**
| Diff | Count | Fixable? |
|---|---|---|
| `bool` → `new bool` | ~7 | No (needs class hierarchy) |
| `UIView` → `IProtocol` | ~5 | Partially (protocol inference) |
| `PSPDFPageIndex` → `nuint` | ~3 | No (cross-framework typedef) |
| `NSObject[]` → `Type[]` | ~4 | Yes (typed arrays) |

### Dimension 5: Parameter Type Differences

For each shared export, parse parameters and compare types:

```python
def extract_params(decl):
    m = re.search(r'\(([^)]*)\)', decl)
    if m:
        params = m.group(1).strip()
        return [p.strip().rsplit(' ', 1)[0].replace('[NullAllowed] ', '') 
                for p in params.split(',') if p.strip()]
    return []
```

**Common param type diff categories:**
| Diff | Cause | Fixable? |
|---|---|---|
| `PSPDFPageIndex` → `nuint` | Cross-framework typedef | No |
| `NSObject` → `IProtocol` | Protocol type inference | Yes |
| `NSObject[]` → `Type[]` | Typed array inference | Yes |
| `Action` → `Action<bool>` | Block type parsing | Partially |
| `string` → `[BindAs]` | Enum-backed strings | No (sharpie-specific) |

## Interpreting Results

### Attribute count accuracy targets

| Attribute | Target | Notes |
|---|---|---|
| `[Export]` | 99-100% | Missing = parse failures or API version diffs |
| `[Abstract]` | 100-101% | Should never be missing |
| `[Constructor]` | 95-100% | `nonnull instancetype` handling matters |
| `[NullAllowed]` | 100-105% | Slight over-annotation is acceptable |
| `[Async]` | 100-160% | We detect more completion handlers than sharpie |
| `[Protocol]` | ~61% | NOT a real gap — sharpie double-counts with `#if NET` |

### Known unfixable gaps (PSPDFKitUI)

- **35 missing exports**: API version differences (17), builder pattern inheritance (4), EventArgs (3), UINavigationItem overrides (2), cross-framework (9)
- **`[Wrap]` (29)**: Requires enum constant mapping knowledge
- **`new` modifier (12)**: Requires class hierarchy analysis
- **Cross-framework typedefs (22)**: Would need all framework headers

### What to investigate

1. **New missing exports** (compared to previous run): likely a parser regression
2. **Method naming regressions**: check if a naming rule change helped some but hurt others
3. **New `unsafe` keywords**: likely a type mapping producing raw pointers
4. **`instancetype` in non-comment lines**: constructor detection may have regressed

## Example Full Comparison Script

```python
#!/usr/bin/env python3
"""Compare two C# API definition files."""
import re, sys
from collections import Counter

def extract_attributes(filepath):
    with open(filepath) as f:
        content = f.read()
    patterns = {
        'Export': r'Export\s*\(', 'BaseType': r'BaseType\s*\(',
        'Protocol': r'\[Protocol\b', 'Abstract': r'\[Abstract\]',
        'Static': r'\[Static\]', 'NullAllowed': r'NullAllowed',
        'Field': r'Field\s*\(', 'Async': r'\[Async\]',
        'Notification': r'\[Notification\b',
        'Constructor': r'NativeHandle Constructor',
        'DesignatedInitializer': r'\[DesignatedInitializer\]',
    }
    attrs = {name: len(re.findall(p, content)) for name, p in patterns.items()}
    attrs['interfaces'] = len(re.findall(
        r'^\s*(?:partial\s+)?interface\s+\w+', content, re.MULTILINE))
    return attrs

def extract_exports(filepath):
    with open(filepath) as f:
        return set(re.findall(r'Export\s*\("([^"]+)"', f.read()))

def get_export_to_decl(text):
    lines = text.split('\n')
    results = {}
    for i, line in enumerate(lines):
        m = re.search(r'Export\s*\("([^"]+)"', line)
        if m:
            sel = m.group(1)
            for j in range(i+1, min(i+5, len(lines))):
                ns = lines[j].strip()
                if not ns.startswith('[') and not ns.startswith('//') and ns:
                    results[sel] = ns
                    break
    return results

ref_file, our_file = sys.argv[1], sys.argv[2]

# 1. Attribute counts
ref_attrs = extract_attributes(ref_file)
our_attrs = extract_attributes(our_file)
print(f"{'Attribute':<25} {'Reference':>10} {'Ours':>10} {'Delta':>10} {'%':>8}")
print("-" * 67)
for key in sorted(set(list(ref_attrs) + list(our_attrs))):
    r, o = ref_attrs.get(key, 0), our_attrs.get(key, 0)
    pct = f"{o/r*100:.0f}%" if r > 0 else "N/A"
    flag = "✓" if o - r >= 0 else "✗"
    print(f"{key:<25} {r:>10} {o:>10} {o-r:>+10} {pct:>8} {flag}")

# 2. Missing exports
missing = sorted(extract_exports(ref_file) - extract_exports(our_file))
print(f"\nMissing exports: {len(missing)}")

# 3. Method naming
with open(ref_file) as f: ref_text = f.read()
with open(our_file) as f: our_text = f.read()
ref_decls = get_export_to_decl(ref_text)
our_decls = get_export_to_decl(our_text)
common = set(ref_decls) & set(our_decls)

def name_of(decl):
    parts = decl.split('(', 1)
    return parts[0].strip().split()[-1] if parts[0].strip() else ''

matches = sum(1 for s in common if name_of(ref_decls[s]) == name_of(our_decls[s]))
print(f"\nMethod naming: {matches}/{len(common)} ({100*matches//len(common)}%)")

# 4. Return type + param diffs
ret_diffs = param_diffs = 0
for sel in common:
    rd, od = ref_decls[sel], our_decls[sel]
    r_ret = rd.split('(')[0].strip().rsplit(' ', 1)[0] if '(' in rd else ''
    o_ret = od.split('(')[0].strip().rsplit(' ', 1)[0] if '(' in od else ''
    if r_ret and o_ret and r_ret != o_ret: ret_diffs += 1
    rm = re.search(r'\(([^)]*)\)', rd)
    om = re.search(r'\(([^)]*)\)', od)
    if rm and om and rm.group(1).strip() != om.group(1).strip(): param_diffs += 1

print(f"Return type diffs: {ret_diffs}")
print(f"Param type diffs: {param_diffs}")
```

Usage:
```bash
python3 compare_bindings.py reference/ApiDefinition.cs generated/ApiDefinition.cs
```
