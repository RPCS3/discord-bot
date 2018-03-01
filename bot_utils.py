from api.request import ApiRequest
from api.result import ApiResult

def get_code(code: str) -> ApiResult:
    """
    Gets the game data for a certain game code or returns None
    :param code: code to get data for
    :return: data or None
    """
    result = ApiRequest().set_search(code).set_amount(10).request()
    if len(result.results) >= 1:
        for result in result.results:
            if result.game_id == code:
                return result
    return ApiResult(code, dict({"status": "Unknown"}))
