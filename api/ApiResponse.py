"""
ApiResponse class
"""
import json
from array import array
from typing import Dict, List

from api import ApiResult, newline_separator, return_codes


class ApiResponse(object):
	"""
	API Response object
	"""

	def __init__(self, data: Dict, amount_wanted: int = None) -> None:
		self.results: List[ApiResult] = array()

		parsed_data = json.loads(data)
		self.code = parsed_data["return_code"]
		self.load_results(parsed_data["results"], amount=amount_wanted)

	def load_results(self, data: Dict, amount: int = None) -> None:
		"""
		Loads the result object from JSON
		:param data: data for the result objects
		:param amount: desired amount to load
		"""
		for game_id, result_data in data:
			if amount is None or len(self.results) < amount:
				self.results.append(ApiResult(game_id, result_data))
			else:
				break

	def to_string(self) -> str:
		"""
		Makes a string representation of the object.
		:return: string representation of the object
		"""
		header_string = "{requestor} searched for: {search_term}"
		results_string = ""

		results_string_part = "```\n"
		for result in self.results:
			result_string = result.to_string()

			if len(results_string) + len(result_string) + 4 > 2000:
				results_string_part += "```"
				results_string += results_string_part + newline_separator
				results_string_part = "```\n"

			results_string_part += results_string_part + '\n'

		if results_string_part != "```\n":
			results_string_part += "```"
			results_string += results_string_part

		footer_string = "Retrieved from: {request_url}"
		if return_codes[self.code]["display_results"]:
			return "{}{}{}{}{}".format(
				header_string + '\n' + return_codes[self.code]["info"],
				newline_separator,
				results_string,
				newline_separator,
				footer_string
			)
		elif return_codes[self.code]["override_all"]:
			return return_codes[self.code]["info"]
		else:
			return "{}{}".format(
				header_string + '\n' + return_codes[self.code]["info"],
				(newline_separator + footer_string) if return_codes[self.code]["display_footer"] else ""
			)
