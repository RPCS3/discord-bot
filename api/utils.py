import time


def trim_string(string: str, length: int) -> str:
	if len(string) > length:
		return string[:length - 3] + "..."
	else:
		return string


def system_time_millis() -> int:
	return int(round(time.time() * 1000))
