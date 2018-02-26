"""
API Configuration File
"""
version = 1

datetime_input_format = "%Y-%m-%d"
datetime_output_format = "%Y-%m-%d"
datetime_compatlist_query_format = "%Y%m%d"
base_url = "https://rpcs3.net/compatibility"
newline_separator = "<newline>"

return_codes = {
    0: {
        "display_results": True,
        "override_all": False,
        "display_footer": True,
        "info": "Results successfully retrieved."
    },
    1: {
        "display_results": False,
        "override_all": False,
        "display_footer": True,
        "info": "No results."
    },
    2: {
        "display_results": True,
        "override_all": False,
        "display_footer": True,
        "info": "No match was found, displaying results for: ***{lehvenstein}***."
    },
    -1: {
        "display_results": False,
        "override_all": True,
        "display_footer": False,
        "info": "{requestor}: Internal error occurred, please contact Ani and Nicba1010"
    },
    -2: {
        "display_results": False,
        "override_all": True,
        "display_footer": False,
        "info": "{requestor}: API is under maintenance, please try again later."
    },
    -3: {
        "display_results": False,
        "override_all": False,
        "display_footer": False,
        "info": "Illegal characters found, please try again with a different search term."
    }
}

default_amount = 1
request_result_amount = {
    1: 15,
    2: 25,
    3: 50,
    4: 100
}

directions = {
    "a": ("a", "asc", "ascending"),
    "d": ("d", "desc", "descending")
}

regions = {
    "j": ("j", "ja", "japan", "JPN"),
    "u": ("u", "us", "america", "USA"),
    "e": ("e", "eu", "europe", "EU"),
    "a": ("a", "asia", "ch", "china", "CHN"),
    "k": ("k", "kor", "korea", "KOR"),
    "h": ("h", "hk", "hong", "kong", "hongkong", "HK")
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
    "date": 4
}

release_types = {
    "b": ("b", "d", "disc", "bluray", "Blu-Ray"),
    "n": ("n", "p", "psn", "PSN")
}
