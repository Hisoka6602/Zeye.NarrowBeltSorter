#!/usr/bin/env python3
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
INSTRUCTIONS_PATH = REPO_ROOT / ".github" / "copilot-instructions.md"
CHINESE_CHAR_PATTERN = re.compile(r"[\u4e00-\u9fff]")
HISTORY_SECTION_HEADER = "## 历史更新记录"
MAX_PREVIEW_LENGTH = 120
ELLIPSIS = "..."
# 重复代码判定最小长度：过滤短语句误报，聚焦复制粘贴片段。
MIN_DUPLICATE_LINE_LENGTH = 30
# 重复代码跨文件阈值：至少 3 个文件同时出现才判定高风险重复。
MIN_DUPLICATE_FILE_COUNT = 3
# 复杂方法阈值：超过该行数且缺少“步骤”注释时提示补充。
COMPLEX_METHOD_LINE_THRESHOLD = 35
# 工具类体量阈值：超过阈值时提示拆分以提升复用与可维护性。
UTILITY_CLASS_MAX_LINES = 350
UTILITY_CLASS_MAX_METHODS = 20
# 方法体扫描窗口：用于复杂方法步骤注释检测。
METHOD_WINDOW_SIZE = 120
# 枚举成员注释回看行数：用于判定 Description 与注释是否紧邻声明。
ENUM_ATTRIBUTE_LOOKBACK_LINES = 4
# 重复片段预览长度：避免错误信息过长影响可读性。
DUPLICATE_CODE_PREVIEW_LENGTH = 80
STEP_HINT_KEYWORDS = ("步骤", "Step", "流程")
METHOD_DECLARATION_EXCLUDED_KEYWORDS = (
    "if",
    "for",
    "foreach",
    "while",
    "switch",
    "catch",
    "lock",
    "using",
    "return",
    "throw",
    "else",
)
METHOD_DECLARATION_EXCLUDED_PATTERN = "|".join(re.escape(item) for item in METHOD_DECLARATION_EXCLUDED_KEYWORDS)

AUTOMATED_RULES = set(range(1, 40))
MANUAL_RULES: set[int] = set()

EXPECTED_RULE_TEXTS = {
    1: "全项目禁止使用 UTC 时间语义和 UTC 相关 API；统一使用本地时间（Local Time）语义。",
    2: "任何新增或修改涉及时间的代码，必须保持本地时间语义一致，不得引入 UTC 转换链路。读取配置中的时间字符串时，默认按本地时间解析；示例配置不得使用 `Z` 或 offset（如 `+08:00`）。",
    3: "每次新增文件或删除文件后，必须同步更新仓库根目录 `README.md` 中用于逐项说明目录/文件职责的文件树章节，保证职责说明与仓库实际内容一致。",
    4: "所有从 doc/pdf 文档解析到 md 的内容都必须能在原文档中找到出处。",
    5: "所有的方法都需要有注释，复杂的实现方法必须要有步骤注释。",
    6: "全局禁止代码重复（影分身代码/复制粘贴代码）。",
    7: "小工具类尽量代码简洁和做到高性能、高复用。",
    8: "所有枚举都需要定义在 `Zeye.NarrowBeltSorter.Core.Enums` 的子目录下(非常重要)。",
    9: "所有枚举都必须包含 `Description` 和注释。",
    10: "所有事件载荷都必须定义在 `Events` 的子目录下。",
    11: "事件载荷需要使用 `readonly record struct`（确保不可变、值语义与更优内存性能）。",
    12: "所有的异常都必须输出日志。",
    13: "整个项目只能有一个安全执行器（使用 Zeye.NarrowBeltSorter.Core.Utilities.SafeExecutor），通过现有依赖注入单例统一访问，不得新增并行实现。",
    14: "Copilot 的回答/描述/交流都需要使用中文。",
    15: "日志只能使用 NLog，日志不能影响程序性能（无论日志输出多频繁）。",
    16: "Copilot 每次修改代码后都需要检查是否影分身代码，如果有则需要删除修复",
    17: "项目默认允许并要求 Copilot 自动创建拉取请求（PR），无需额外人工切换开关",
    18: "严格划分结构层级边界，尽量做到0入侵（非常重要）",
    19: "有性能更高的特性标记需要尽量使用，追求极致性能",
    20: "注释中禁止出现第二人称的字眼",
    21: "对字段、类型、文件、项目的命名有严格要求，必须符合专业领域术语",
    22: "历史更新记录不要写在 README.md 中（禁止长期累积历史记录）。",
    23: "相同意义的工具代码需要提取集中,不可以到处实现",
    24: "swagger的所有参数、方法、枚举项都必须要有中文注释",
    25: "每个类都需要独立的文件,不能多个类放在同一个文件内",
    26: "md 文件除 README.md 外，其他 md 文件都需要使用中文命名（固定约定文件 `.github/copilot-instructions.md` 例外）。",
    27: "禁止使用过时标记去标记代码,如果代码已过时则必须删除,调用新的实现",
    28: "读写 Modbus 需要使用 TouchSocket.Modbus 库",
    29: "重试策略需要使用 Polly 库",
    30: "读写 TCP 需要使用 TouchSocket 库",
    31: "强制：`appsettings.json` 与 `appsettings.Development.json` 的每个字段都必须有中文注释。",
    32: "强制：所有 Options 类型必须定义在 `Zeye.NarrowBeltSorter.Core.Options` 目录或子目录。",
    33: "强制：所有 interface 必须定义在 `Zeye.NarrowBeltSorter.Core` 子目录。",
    34: "强制：所有静态工具类必须定义在 `Zeye.NarrowBeltSorter.Core.Utilities` 目录或子目录。",
    35: "强制：每次改动必须检查并修复违反 `.github/copilot-instructions.md` 的项。",
    36: "强制：能用 `var` 的地方尽量用 `var`。",
    37: "强制：危险代码必须通过统一隔离器（`SafeExecutor`）执行。",
    38: "强制：修改完成后默认自动创建 PR。",
    39: "强制：Host 层禁止使用 Servers 目录命名，统一使用 Services",
}

FORBIDDEN_UTC_PATTERNS = [
    (re.compile(r"\bDateTimeOffset\b"), "禁止引入 DateTimeOffset"),
    (re.compile(r"\bDateTime\.UtcNow\b"), "禁止使用 DateTime.UtcNow"),
    (re.compile(r"\bToUniversalTime\s*\("), "禁止引入 UTC 转换链路"),
    (re.compile(r"\bFromUnixTime"), "禁止引入 UTC 时间转换 API"),
    (re.compile(r"\bUTC\b"), "禁止新增 UTC 语义"),
    (
        re.compile(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?Z\b"),
        "示例时间禁止使用 Z",
    ),
    (re.compile(r"[+-]\d{2}:\d{2}\b"), "示例时间禁止使用 offset"),
]

SECOND_PERSON_PATTERN = re.compile(r"(你|您|you|your)", re.IGNORECASE)
FORBIDDEN_LOGGER_PATTERNS = [
    re.compile(r"\bSerilog\b"),
    re.compile(r"\blog4net\b", re.IGNORECASE),
    re.compile(r"\bConsole\.Write(Line)?\s*\("),
    re.compile(r"\bDebug\.Write(Line)?\s*\("),
]

ENUM_DECLARATION_PATTERN = re.compile(r"^\s*(?:public|internal|private|protected)?\s*enum\s+\w+")
ENUM_MEMBER_PATTERN = re.compile(r"^\s*([A-Za-z_]\w*)\s*(?:=\s*[^,]+)?\s*,?\s*$")
METHOD_DECLARATION_PATTERN = re.compile(
    r"^\s*(?!await\b)(?!return\b)(?=(?:(?:public|private|protected|internal|static|async)\b|[\w<>\[\],\.\?]+\s+[A-Za-z_]\w*\s*\())"
    r"(?:(?:public|private|protected|internal)\s+)?"
    r"(?:static\s+)?(?:async\s+)?(?:[\w<>\[\],\.\?]+\s+)?"
    rf"(?!{METHOD_DECLARATION_EXCLUDED_PATTERN}\b)"
    r"[A-Za-z_]\w*\s*\([^;]*\)\s*(?:\{|=>|;)"
)


def is_ignored_file(path: str) -> bool:
    """判断是否为无需参与规则扫描的文件。"""
    normalized = path.replace("\\", "/")
    return normalized.startswith(".github/") or normalized.startswith("obj/") or normalized.startswith("bin/")


def run_git(args: list[str]) -> str:
    """执行 git 命令并返回标准输出文本。

    当 git 命令执行失败时抛出 RuntimeError。
    """
    try:
        completed = subprocess.run(
            ["git", *args],
            cwd=REPO_ROOT,
            check=True,
            capture_output=True,
            text=True,
        )
        return completed.stdout
    except subprocess.CalledProcessError as error:
        command = "git " + " ".join(args)
        stderr_text = (error.stderr or "").strip()
        message = (
            f"执行命令失败：{command}\n"
            f"返回码：{error.returncode}\n"
            f"错误输出：{stderr_text or '无'}"
        )
        raise RuntimeError(message) from error


def parse_rules() -> dict[int, str]:
    """从 copilot 规则文件中解析“数字编号规则”集合。

    返回值为字典：key=规则编号，value=规则文本。
    """
    content = INSTRUCTIONS_PATH.read_text(encoding="utf-8")
    rules: dict[int, str] = {}
    for line in content.splitlines():
        match = re.match(r"^(\d+)\.\s+(.+)$", line.strip())
        if match is None:
            continue
        rules[int(match.group(1))] = match.group(2).strip()
    return rules


def parse_changed_files(base_ref: str, head_ref: str) -> list[tuple[str, list[str]]]:
    """解析两个引用之间的文件变更状态与路径。

    返回值为列表，每项结构为 `(状态码, 文件路径列表)`，状态码示例：`A`（新增）、`D`（删除）。
    """
    diff_text = run_git(["diff", "--name-status", f"{base_ref}...{head_ref}"])
    changes: list[tuple[str, list[str]]] = []
    for raw in diff_text.splitlines():
        if not raw.strip():
            continue
        parts = raw.split("\t")
        status = parts[0]
        paths = parts[1:]
        changes.append((status, paths))
    return changes


def flatten_changed_paths(changes: list[tuple[str, list[str]]]) -> set[str]:
    """展开变更记录中的文件路径集合。"""
    paths: set[str] = set()
    for _, file_paths in changes:
        for path in file_paths:
            paths.add(path.replace("\\", "/"))
    return paths


def parse_added_lines_by_file(base_ref: str, head_ref: str) -> dict[str, list[str]]:
    """解析 diff 中新增行，按文件聚合。"""
    diff_text = run_git(["diff", "--no-color", "--unified=0", f"{base_ref}...{head_ref}"])
    result: dict[str, list[str]] = {}
    current_file: str | None = None
    for raw_line in diff_text.splitlines():
        if raw_line.startswith("+++ b/"):
            current_file = raw_line[6:].replace("\\", "/")
            result.setdefault(current_file, [])
            continue
        if raw_line.startswith("+++ "):
            current_file = None
            continue
        if current_file is None:
            continue
        if raw_line.startswith("+") and not raw_line.startswith("+++"):
            result[current_file].append(raw_line[1:])
    return result


def get_changed_files_by_suffix(
    changes: list[tuple[str, list[str]]],
    suffixes: tuple[str, ...],
) -> list[str]:
    """根据后缀筛选新增/修改/重命名文件。"""
    files: list[str] = []
    for status, paths in changes:
        if not status.startswith(("A", "M", "R", "C")):
            continue
        target_path = paths[-1].replace("\\", "/")
        if is_ignored_file(target_path):
            continue
        if target_path.endswith(suffixes):
            files.append(target_path)
    return sorted(set(files))


def read_repo_file(path: str) -> str:
    """读取仓库内文件内容。"""
    return (REPO_ROOT / path).read_text(encoding="utf-8")


def get_method_block_line_count(lines: list[str], start_index: int) -> int:
    """计算方法体实际行数。

    参数 `start_index` 为方法声明所在行索引；返回值为从该行开始到方法体闭合的行数（含起始行）。
    当方法体无法完整闭合时，返回剩余行数与扫描窗口上限的较小值。
    """
    brace_depth = 0
    body_started = False
    for index in range(start_index, len(lines)):
        current_line = lines[index]
        # 先剔除行尾注释，避免注释文本中的花括号干扰方法体闭合计数。
        line_without_comment = re.sub(r"//.*$", "", current_line)
        # 字符串字面量替换为等长占位引号，保留语法骨架并屏蔽字符串内部花括号。
        line_for_count = re.sub(r'"(?:\\.|[^"\\])*"', '""', line_without_comment)
        # 字符字面量同样替换为占位，统一屏蔽转义字符与花括号噪声。
        line_for_count = re.sub(r"'(?:\\.|[^'\\])'", "''", line_for_count)
        open_count = line_for_count.count("{")
        close_count = line_for_count.count("}")
        if open_count > 0:
            body_started = True
        brace_depth += open_count
        brace_depth -= close_count
        if body_started and brace_depth == 0:
            return index - start_index + 1
        if body_started and brace_depth < 0:
            # 当深度出现负值时视为局部语法异常，终止精确计数并走窗口回退策略。
            break
    return min(METHOD_WINDOW_SIZE, len(lines) - start_index)


def check_rule_coverage(rules: dict[int, str], errors: list[str]) -> None:
    """校验规则文件编号与校验器声明集合是否一致。

    Args:
        rules: 已解析的规则字典。
        errors: 错误消息列表（原地追加）。
    """
    discovered = set(rules)
    declared = AUTOMATED_RULES | MANUAL_RULES
    if discovered != declared:
        missing = sorted(discovered - declared)
        extra = sorted(declared - discovered)
        if missing:
            errors.append(
                "发现新增/变更的 Copilot 编号规则未在校验器声明，请同步更新 "
                f"validate_copilot_rules.py：缺失规则 {missing}"
            )
        if extra:
            errors.append(
                "校验器中声明了已不存在的规则编号，请同步清理 "
                f"validate_copilot_rules.py：多余规则 {extra}"
            )


def check_rule_text_sync(rules: dict[int, str], errors: list[str]) -> None:
    """校验规则文本与校验器内置快照一致，确保规则更新时强制同步校验逻辑。"""
    for rule_id in sorted(EXPECTED_RULE_TEXTS):
        expected = EXPECTED_RULE_TEXTS[rule_id].strip()
        actual = rules.get(rule_id, "").strip()
        if expected != actual:
            errors.append(
                f"规则 {rule_id} 文本已变更，请同步更新 validate_copilot_rules.py 的 EXPECTED_RULE_TEXTS 与校验实现。"
            )


def check_rule_3(changes: list[tuple[str, list[str]]], errors: list[str]) -> None:
    """校验新增/删除文件时 README.md 是否同步更新。

    Args:
        changes: 文件变更列表。
        errors: 错误消息列表（原地追加）。
    """
    has_add_or_delete = False
    readme_touched = False
    for status, paths in changes:
        if status.startswith(("A", "D")):
            has_add_or_delete = True
        if any(path == "README.md" for path in paths):
            readme_touched = True
    if has_add_or_delete and not readme_touched:
        errors.append("检测到新增/删除文件，但 README.md 未同步更新（规则 3）。")


def check_rule_26(errors: list[str]) -> None:
    """校验除 README 与例外文件外的 md 文件名是否包含中文。

    Args:
        errors: 错误消息列表（原地追加）。
    """
    md_files = run_git(["-c", "core.quotepath=false", "ls-files", "--", "*.md"]).splitlines()
    for path in md_files:
        normalized = path.replace("\\", "/")
        if normalized in {"README.md", ".github/copilot-instructions.md"}:
            continue
        file_name = Path(normalized).name
        if not CHINESE_CHAR_PATTERN.search(file_name):
            errors.append(f"Markdown 文件命名不符合规则 26：{normalized}")


def check_rule_1_2(base_ref: str, head_ref: str, errors: list[str]) -> None:
    """校验新增内容未引入 UTC 语义、DateTimeOffset 与时间偏移示例。

    Args:
        base_ref: diff 基线引用。
        head_ref: diff 头引用。
        errors: 错误消息列表（原地追加）。
    """
    added_lines_by_file = parse_added_lines_by_file(base_ref, head_ref)
    for path, lines in added_lines_by_file.items():
        if is_ignored_file(path):
            continue
        for index, line in enumerate(lines, start=1):
            for pattern, hint in FORBIDDEN_UTC_PATTERNS:
                if pattern.search(line):
                    preview = line.strip()
                    if len(preview) > MAX_PREVIEW_LENGTH:
                        preview = preview[: MAX_PREVIEW_LENGTH - len(ELLIPSIS)] + ELLIPSIS
                    errors.append(f"规则 1/2 违规（{path} 新增行#{index}）：{hint} -> {preview}")
                    break


def check_rule_22(errors: list[str]) -> None:
    """校验 README.md 不包含历史更新记录章节。

    Args:
        errors: 错误消息列表（原地追加）。
    """
    readme = (REPO_ROOT / "README.md").read_text(encoding="utf-8")
    if HISTORY_SECTION_HEADER in readme:
        errors.append("README.md 包含“历史更新记录”章节，违反规则 22。")


def check_rule_4(changed_md_files: list[str], errors: list[str]) -> None:
    """校验 doc/pdf 解析文档包含可追溯来源。"""
    for path in changed_md_files:
        normalized = path.replace("\\", "/")
        if "/doc/" not in normalized:
            continue
        content = read_repo_file(normalized)
        if "来源：" not in content and "文档来源：" not in content:
            errors.append(f"规则 4 违规：文档缺少来源标注 -> {normalized}")
        if ".pdf" not in content and ".doc" not in content and ".xlsx" not in content:
            errors.append(f"规则 4 违规：文档未指向可追溯原文档 -> {normalized}")


def check_rule_5(changed_cs_files: list[str], errors: list[str]) -> None:
    """校验方法注释与复杂方法步骤注释。"""
    for path in changed_cs_files:
        lines = read_repo_file(path).splitlines()
        for index, line in enumerate(lines):
            if not METHOD_DECLARATION_PATTERN.match(line):
                continue
            cursor = index - 1
            has_doc_comment = False
            while cursor >= 0 and lines[cursor].strip():
                stripped = lines[cursor].strip()
                if stripped.startswith("///"):
                    has_doc_comment = True
                    cursor -= 1
                    continue
                if stripped.startswith("[") or stripped.startswith("#"):
                    cursor -= 1
                    continue
                break
            if not has_doc_comment:
                errors.append(f"规则 5 违规：方法缺少 XML 注释 -> {path}:{index + 1}")
                continue

            method_line_count = get_method_block_line_count(lines, index)
            if method_line_count >= COMPLEX_METHOD_LINE_THRESHOLD:
                end = min(index + method_line_count, len(lines))
                method_window = "\n".join(lines[index:end])
                if not any(keyword in method_window for keyword in STEP_HINT_KEYWORDS):
                    errors.append(f"规则 5 违规：复杂方法缺少步骤注释 -> {path}:{index + 1}")


def check_duplicate_code_and_scattered_utilities(
    added_lines: dict[str, list[str]],
    errors: list[str],
) -> None:
    """校验复制粘贴与工具代码分散的高风险迹象。"""
    line_to_paths: dict[str, set[str]] = {}
    for path, lines in added_lines.items():
        if not path.endswith(".cs") or is_ignored_file(path):
            continue
        for raw_line in lines:
            normalized = raw_line.strip()
            if (
                len(normalized) < MIN_DUPLICATE_LINE_LENGTH
                or normalized.startswith("//")
                or normalized.startswith("namespace ")
                or normalized.startswith("using ")
                or normalized in {"{", "}", "};"}
            ):
                continue
            line_to_paths.setdefault(normalized, set()).add(path)

    for text, paths in line_to_paths.items():
        if len(paths) >= MIN_DUPLICATE_FILE_COUNT:
            preview = (
                text[:DUPLICATE_CODE_PREVIEW_LENGTH] + ELLIPSIS
                if len(text) > DUPLICATE_CODE_PREVIEW_LENGTH
                else text
            )
            errors.append(
                "规则 6/16/23 违规：检测到多文件重复新增代码，请抽取复用工具。"
                f" 片段：{preview}"
            )
            break


def check_rule_7(changed_cs_files: list[str], errors: list[str]) -> None:
    """校验小工具类复杂度与复用倾向。"""
    utility_name = re.compile(r"\b(class|record)\s+\w*(Utility|Helper|Util|Tools)\b")
    for path in changed_cs_files:
        lines = read_repo_file(path).splitlines()
        joined = "\n".join(lines)
        if not utility_name.search(joined):
            continue
        method_count = sum(1 for line in lines if METHOD_DECLARATION_PATTERN.match(line))
        if len(lines) > UTILITY_CLASS_MAX_LINES or method_count > UTILITY_CLASS_MAX_METHODS:
            errors.append(f"规则 7 违规：工具类过重，建议拆分复用 -> {path}")


def check_rule_8_9(changed_cs_files: list[str], errors: list[str]) -> None:
    """校验枚举目录、Description 与注释。"""
    for path in changed_cs_files:
        lines = read_repo_file(path).splitlines()
        has_enum = any(ENUM_DECLARATION_PATTERN.match(line) for line in lines)
        if not has_enum:
            continue
        normalized = path.replace("\\", "/")
        if "Zeye.NarrowBeltSorter.Core/Enums/" not in normalized:
            errors.append(f"规则 8 违规：枚举未定义在 Core/Enums 子目录 -> {path}")

        inside_enum = False
        enum_body_started = False
        brace_depth = 0
        for index, line in enumerate(lines):
            if ENUM_DECLARATION_PATTERN.match(line):
                inside_enum = True
                brace_depth = line.count("{") - line.count("}")
                continue
            if not inside_enum:
                continue
            brace_depth += line.count("{")
            brace_depth -= line.count("}")
            if not enum_body_started:
                if brace_depth > 0:
                    enum_body_started = True
                else:
                    continue
            if brace_depth == 0:
                inside_enum = False
                enum_body_started = False
                continue
            stripped = line.strip()
            if not stripped or stripped.startswith("//") or stripped.startswith("///") or stripped.startswith("["):
                continue
            if ENUM_MEMBER_PATTERN.match(line):
                previous = "\n".join(lines[max(0, index - ENUM_ATTRIBUTE_LOOKBACK_LINES):index])
                if "[Description(" not in previous:
                    errors.append(f"规则 9 违规：枚举项缺少 Description -> {path}:{index + 1}")
                if "///" not in previous:
                    errors.append(f"规则 9 违规：枚举项缺少注释 -> {path}:{index + 1}")


def check_rule_10_11(changed_cs_files: list[str], errors: list[str]) -> None:
    """校验事件载荷目录与 readonly record struct。"""
    event_payload_pattern = re.compile(r"\b(record\s+struct|readonly\s+record\s+struct)\s+(\w*EventArgs)\b")
    for path in changed_cs_files:
        content = read_repo_file(path)
        for match in event_payload_pattern.finditer(content):
            type_name = match.group(2)
            normalized = path.replace("\\", "/")
            if "/Events/" not in normalized:
                errors.append(f"规则 10 违规：事件载荷不在 Events 子目录 -> {path} ({type_name})")
            if "readonly record struct" not in match.group(0):
                errors.append(f"规则 11 违规：事件载荷必须使用 readonly record struct -> {path} ({type_name})")


def check_rule_12(added_lines: dict[str, list[str]], errors: list[str]) -> None:
    """校验新增 catch 代码块包含日志输出。"""
    for path, lines in added_lines.items():
        if not path.endswith(".cs") or is_ignored_file(path):
            continue
        has_added_catch = any(re.search(r"\bcatch\b", line) for line in lines)
        if not has_added_catch:
            continue
        has_added_log = any(
            "_logger." in line or "logger." in line or ".Log" in line
            for line in lines
        )
        if not has_added_log:
            errors.append(f"规则 12 违规：新增 catch 但未检测到日志输出 -> {path}")


def check_rule_13(added_lines: dict[str, list[str]], errors: list[str]) -> None:
    """校验未新增并行安全执行器实现。"""
    for path, lines in added_lines.items():
        if not path.endswith(".cs") or is_ignored_file(path):
            continue
        normalized = path.replace("\\", "/")
        segments = [segment for segment in normalized.split("/") if segment]
        if any(segment.endswith(".Tests") for segment in segments):
            continue
        for line in lines:
            if re.search(r"\b(class|record|struct)\s+\w*SafeExecutor\b", line):
                if "Zeye.NarrowBeltSorter.Core.Utilities.SafeExecutor" not in line:
                    errors.append(f"规则 13 违规：禁止新增 SafeExecutor 并行实现 -> {path}")
            if re.search(r"\bnew\s+\w*SafeExecutor\s*\(", line):
                errors.append(f"规则 13 违规：禁止直接 new 其他 SafeExecutor -> {path}")


def check_rule_14(added_lines: dict[str, list[str]], errors: list[str]) -> None:
    """校验新增沟通性文本优先中文。"""
    for path, lines in added_lines.items():
        if not path.endswith((".md", ".txt", ".cs")):
            continue
        for line in lines:
            stripped = line.strip()
            is_comment = (
                (path.endswith((".md", ".txt")) and stripped.startswith("#"))
                or stripped.startswith("//")
                or stripped.startswith("///")
                or stripped.startswith("*")
            )
            if not is_comment:
                continue
            readable_text = re.sub(r"<[^>]+>", "", stripped).strip()
            if not readable_text:
                continue
            if re.search(r"[A-Za-z]{4,}", readable_text) and not CHINESE_CHAR_PATTERN.search(readable_text):
                errors.append(f"规则 14 违规：新增说明性文本应使用中文 -> {path}")
                break


def check_rule_15(added_lines: dict[str, list[str]], errors: list[str]) -> None:
    """校验日志实现限制。"""
    for path, lines in added_lines.items():
        if not path.endswith(".cs") or is_ignored_file(path):
            continue
        for line in lines:
            for pattern in FORBIDDEN_LOGGER_PATTERNS:
                if pattern.search(line):
                    errors.append(f"规则 15 违规：检测到非约定日志用法 -> {path}: {line.strip()}")
                    break


def check_rule_17(errors: list[str]) -> None:
    """规则 17 为流程约定，当前无可稳定自动化机检项，保留参数以便后续扩展。"""
    _ = errors


def check_rule_18(changed_paths: set[str], errors: list[str]) -> None:
    """校验核心层依赖边界（最小可机检版本）。"""
    for path in sorted(changed_paths):
        normalized = path.replace("\\", "/")
        if not normalized.endswith(".csproj") or is_ignored_file(normalized):
            continue
        content = read_repo_file(normalized)
        if normalized.endswith("Zeye.NarrowBeltSorter.Core/Zeye.NarrowBeltSorter.Core.csproj"):
            if "<ProjectReference" in content:
                errors.append("规则 18 违规：Core 层不应新增项目依赖引用。")


def check_rule_19(changed_cs_files: list[str], errors: list[str]) -> None:
    """校验明显可优化但未使用高性能特性标记的场景。"""
    for path in changed_cs_files:
        content = read_repo_file(path)
        if "record struct" in content and "readonly record struct" not in content and "EventArgs" in content:
            errors.append(f"规则 19 违规：事件载荷建议使用 readonly record struct 以提升性能 -> {path}")


def check_rule_20(added_lines: dict[str, list[str]], errors: list[str]) -> None:
    """校验注释中不出现第二人称。"""
    for path, lines in added_lines.items():
        if not path.endswith((".cs", ".md", ".txt")):
            continue
        for line in lines:
            stripped = line.strip()
            if stripped.startswith(("///", "//", "#", "*")) and SECOND_PERSON_PATTERN.search(stripped):
                errors.append(f"规则 20 违规：注释出现第二人称 -> {path}: {stripped}")


def check_rule_21(added_lines: dict[str, list[str]], errors: list[str]) -> None:
    """校验新增命名避免通用占位词。"""
    bad_name_pattern = re.compile(r"\b(Class|Temp|Test|Demo|Sample)\d*\b")
    for path, lines in added_lines.items():
        if not path.endswith(".cs") or is_ignored_file(path):
            continue
        for line in lines:
            if re.search(r"\b(class|record|struct|interface|enum)\b", line) and bad_name_pattern.search(line):
                errors.append(f"规则 21 违规：类型命名不符合专业术语 -> {path}: {line.strip()}")


def check_rule_24(changed_cs_files: list[str], errors: list[str]) -> None:
    """校验 Swagger 注释中文化。"""
    for path in changed_cs_files:
        content = read_repo_file(path)
        if "Swagger" not in content:
            continue
        for line in content.splitlines():
            if "Swagger" in line and '"' in line:
                quoted = re.findall(r'"([^"]*)"', line)
                for text in quoted:
                    if text and not CHINESE_CHAR_PATTERN.search(text):
                        errors.append(f"规则 24 违规：Swagger 文本需中文注释 -> {path}: {line.strip()}")


def check_rule_25(changed_cs_files: list[str], errors: list[str]) -> None:
    """校验单文件单类约束（仅对变更文件）。"""
    type_pattern = re.compile(
        r"^\s*(?:(?:public|internal|private|protected|sealed|abstract|partial)\s+)*"
        r"(class|record|struct)\s+\w+"
    )
    for path in changed_cs_files:
        if path.endswith("AssemblyInfo.cs"):
            continue
        count = 0
        for line in read_repo_file(path).splitlines():
            if type_pattern.match(line):
                count += 1
        if count > 1:
            errors.append(f"规则 25 违规：单文件包含多个类型定义 -> {path}")


def check_rule_27(added_lines: dict[str, list[str]], errors: list[str]) -> None:
    """校验禁止新增 Obsolete 过时标记。"""
    obsolete_pattern = re.compile(r"\[\s*(?:System\.)?Obsolete(?:Attribute)?(?:\s*[\(\]])")
    for path, lines in added_lines.items():
        if not path.endswith(".cs") or is_ignored_file(path):
            continue
        for line in lines:
            if obsolete_pattern.search(line):
                errors.append(f"规则 27 违规：禁止新增过时标记 [Obsolete] -> {path}")
                break


def main() -> int:
    """程序入口：执行规则解析与可自动化规则校验。"""
    parser = argparse.ArgumentParser(description="校验 copilot-instructions 规则合规性")
    parser.add_argument("--base-ref", required=True, help="diff 基线引用，例如 origin/main")
    parser.add_argument("--head-ref", required=True, help="diff 头引用，例如 HEAD")
    args = parser.parse_args()

    rules = parse_rules()
    if not rules:
        print("未能从 .github/copilot-instructions.md 解析出编号规则。", file=sys.stderr)
        return 1

    errors: list[str] = []
    check_rule_coverage(rules, errors)
    check_rule_text_sync(rules, errors)

    changes = parse_changed_files(args.base_ref, args.head_ref)
    changed_paths = flatten_changed_paths(changes)
    added_lines = parse_added_lines_by_file(args.base_ref, args.head_ref)
    changed_md_files = get_changed_files_by_suffix(changes, (".md",))
    changed_cs_files = get_changed_files_by_suffix(changes, (".cs",))

    check_rule_3(changes, errors)
    check_rule_4(changed_md_files, errors)
    check_rule_5(changed_cs_files, errors)
    check_duplicate_code_and_scattered_utilities(added_lines, errors)
    check_rule_7(changed_cs_files, errors)
    check_rule_8_9(changed_cs_files, errors)
    check_rule_10_11(changed_cs_files, errors)
    check_rule_12(added_lines, errors)
    check_rule_13(added_lines, errors)
    check_rule_14(added_lines, errors)
    check_rule_15(added_lines, errors)
    check_rule_17(errors)
    check_rule_18(changed_paths, errors)
    check_rule_19(changed_cs_files, errors)
    check_rule_20(added_lines, errors)
    check_rule_21(added_lines, errors)
    check_rule_26(errors)
    check_rule_27(added_lines, errors)
    check_rule_24(changed_cs_files, errors)
    check_rule_25(changed_cs_files, errors)
    check_rule_1_2(args.base_ref, args.head_ref, errors)
    check_rule_22(errors)

    if errors:
        print("Copilot 规则校验失败：")
        for item in errors:
            print(f"- {item}")
        return 1

    print(
        "Copilot 规则校验通过："
        f" 自动规则 {sorted(AUTOMATED_RULES)}，"
        f" 共解析 {len(rules)} 条编号规则。"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
