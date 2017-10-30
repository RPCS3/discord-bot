"""
ApiResponse class
"""
import json
from typing import Dict, List

from api import newline_separator, return_codes, system_time_millis, regions, release_types
from bot_config import search_header
from .result import ApiResult


class ApiResponse(object):
	"""
	API Response object
	"""

	# noinspection PyUnresolvedReferences
	def __init__(self, request: 'ApiRequest', data: Dict, amount_wanted: int = None, custom_header: str = None) -> None:
		self.request = request
		self.results: List[ApiResult] = []

		parsed_data = json.loads(data)
		self.code = parsed_data["return_code"]
		if return_codes[self.code]["display_results"]:
			self.load_results(parsed_data["results"], amount=amount_wanted)

		self.time_end = system_time_millis()
		self.custom_header = custom_header

	def load_results(self, data: Dict, amount: int = None) -> None:
		"""
		Loads the result object from JSON
		:param data: data for the result objects
		:param amount: desired amount to load
		"""
		for game_id, result_data in data.items():
			if amount is None or len(self.results) < amount:
				self.results.append(ApiResult(game_id, result_data))
			else:
				break

	def to_string(self) -> str:
		"""
		Makes a string representation of the object.
		:return: string representation of the object
		"""
		return self.build_string().format(
			requestor=self.request.requestor.mention,
			search_string=self.request.search,
			request_url=self.request.build_query().replace("&api=v1", ""),
			milliseconds=self.time_end - self.request.time_start,
			amount=self.request.amount_wanted,
			region="" if self.request.region is None else regions[self.request.region][-1],
			media="" if self.request.release_type is None else release_types[self.request.release_type][-1]
		)

	def build_string(self) -> str:
		"""
		Builds a string representation of the object with placeholder.
		:return: string representation of the object with placeholder
		"""
		header_string = search_header if self.custom_header is None else self.custom_header
		results_string = ""

		results_string_part = "```\n"
		for result in self.results:
			result_string = result.to_string()

			if len(results_string_part) + len(result_string) + 4 > 2000:
				results_string_part += "```"
				results_string += results_string_part + newline_separator
				results_string_part = "```\n"

			results_string_part += result_string + '\n'

		if results_string_part != "```\n":
			results_string_part += "```"
			results_string += results_string_part

		footer_string = "Retrieved from: *{request_url}* in {milliseconds} milliseconds!"
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
