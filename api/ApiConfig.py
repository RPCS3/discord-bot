datetime_input_format = "%Y-%m-%d"
datetime_output_format = "%Y-%m-%d"
datetime_compatlist_query_format = "%Y%m%d"
base_url = "https://rpcs3.net/compatibility"

directions = {
	"a": ("a", "asc", "ascending"),
	"d": ("d", "desc", "descending")
}

regions = {
	"j": ("j", "jap", "japan"),
	"u": ("u", "us", "america"),
	"e": ("e", "eu", "europe"),
	"a": ("a", "asia", "ch", "china"),
	"k": ("k", "kor", "korea"),
	"h": ("h", "hk", "hong", "kong", "hongkong")
}

statuses = {
	"all": 0,
	"playable": 1,
	"ingame": 2,
	"intro": 3,
	"loadable": 4,
	"nothing": 5
}

sort_types = {
	"id": 1,
	"title": 2,
	"status": 3,
	"last": 4
}

release_types = {
	"b": ("b", "d", "disc", "bluray"),
	"n": ("n", "p", "psn")
}