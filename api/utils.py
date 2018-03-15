import time


def trim_string(string: str, length: int) -> str:
    if len(string) > length:
        return string[:length - 3] + "..."
    else:
        return string


def system_time_millis() -> int:
    return int(round(time.time() * 1000))


def sanitize_string(s: str) -> str:
    return s.replace("`", "`\u200d").replace("@", "@\u200d") if s is not None else s

