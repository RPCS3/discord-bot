def trim_string(str, len):
	if len(str) > len:
		return str[:len - 3] + "..."
	else:
		return str
