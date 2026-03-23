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

AUTOMATED_RULES = {1, 2, 3, 22, 26}
MANUAL_RULES = {
    4,
    5,
    6,
    7,
    8,
    9,
    10,
    11,
    12,
    13,
    14,
    15,
    16,
    17,
    18,
    19,
    20,
    21,
    23,
    24,
    25,
}

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
    17: "Copilot任务默认由 Copilot 创建拉取请求（PR）",
    18: "严格划分结构层级边界，尽量做到0入侵（非常重要）",
    19: "有性能更高的特性标记需要尽量使用，追求极致性能",
    20: "注释中禁止出现第二人称的字眼",
    21: "对字段、类型、文件、项目的命名有严格要求，必须符合专业领域术语",
    22: "历史更新记录不要写在 README.md 中（禁止长期累积历史记录）。",
    23: "相同意义的工具代码需要提取集中,不可以到处实现",
    24: "swagger的所有参数、方法、枚举项都必须要有中文注释",
    25: "每个类都需要独立的文件,不能多个类放在同一个文件内",
    26: "md 文件除 README.md 外，其他 md 文件都需要使用中文命名（固定约定文件 `.github/copilot-instructions.md` 例外）。",
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
    diff_text = run_git(["diff", "--unified=0", "--no-color", f"{base_ref}...{head_ref}"])
    added_lines = [
        line[1:]
        for line in diff_text.splitlines()
        if line.startswith("+") and not line.startswith("+++")
    ]

    for index, line in enumerate(added_lines, start=1):
        for pattern, hint in FORBIDDEN_UTC_PATTERNS:
            if pattern.search(line):
                preview = line.strip()
                if len(preview) > MAX_PREVIEW_LENGTH:
                    preview = preview[: MAX_PREVIEW_LENGTH - 3] + "..."
                errors.append(f"规则 1/2 违规（新增行#{index}）：{hint} -> {preview}")
                break


def check_rule_22(errors: list[str]) -> None:
    """校验 README.md 不包含历史更新记录章节。

    Args:
        errors: 错误消息列表（原地追加）。
    """
    readme = (REPO_ROOT / "README.md").read_text(encoding="utf-8")
    if HISTORY_SECTION_HEADER in readme:
        errors.append("README.md 包含“历史更新记录”章节，违反规则 22。")


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
    check_rule_3(changes, errors)
    check_rule_26(errors)
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
