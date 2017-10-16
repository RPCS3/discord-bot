"""
ApiRequest class
"""

import html
from datetime import datetime

import requests

import api
from api import datetime_compatlist_query_format, datetime_input_format, base_url, version
from api.response import ApiResponse


class ApiRequest(object):
	"""
	API Request builder object
	"""

	def __init__(self, requestor=None) -> None:
		self.requestor = requestor
		self.custom_header = None
		self.time_start = None
		self.search = None
		self.status = None
		self.start = None
		self.sort = None
		self.date = None
		self.release_type = None
		self.region = None
		self.amount = api.default_amount
		self.amount_wanted = api.request_result_amount[api.default_amount]

	def set_search(self, search: str) -> 'ApiRequest':
		"""
		Adds the search string to the query.
		:param search: string to search for
		:return: ApiRequest object
		"""
		self.search = search
		return self

	def set_custom_header(self, custom_header) -> 'ApiRequest':
		"""
		Sets a custom header.
		:param custom_header: custom hedaer
		:return: ApiRequest object
		"""
		self.custom_header = custom_header

	def set_status(self, status: int) -> 'ApiRequest':
		"""
		Adds status filter to the query.
		:param status: status to filter by, see ApiConfig.statuses
		:return: ApiRequest object
		"""
		try:
			self.status = api.statuses[status]
		except KeyError:
			self.status = None

		return self

	def set_startswith(self, start: str) -> 'ApiRequest':
		"""
		Adds starting character filter to the query.
		:param start: character to filter by
		:return: ApiRequest object
		"""
		if len(start) != 1:
			if start in ("num", "09"):
				self.start = "09"
			elif start in ("sym", "#"):
				self.start = "sym"
		else:
			self.start = start

		return self

	def set_sort(self, sort_type, direction) -> 'ApiRequest':
		"""
		Adds sorting request to query.
		:param sort_type: element to sort by, see ApiConfig.sort_types
		:param direction: sorting direction, see ApiConfig.directions
		:return: ApiRequest object
		"""
		for k, v in api.directions.items():
			if direction in v:
				try:
					self.sort = str(api.sort_types[sort_type]) + k
					return self
				except KeyError:
					self.sort = None
					return self

		return self

	def set_date(self, date: str) -> 'ApiRequest':
		"""
		Adds date filter to query.
		:param date: date to filter by
		:return: ApiRequest object
		"""
		try:
			date = datetime.strptime(date, datetime_input_format)
			self.date = datetime.strftime(date, datetime_compatlist_query_format)
		except ValueError:
			self.date = None

		return self

	def set_release_type(self, release_type: str) -> 'ApiRequest':
		"""
		Adds release type filter to query.
		:param release_type: release type to filter by, see ApiConfig.release_type
		:return: ApiRequest object
		"""
		for k, v in api.release_types.items():
			if release_type in v:
				self.release_type = k
				return self

		self.release_type = None
		return self

	def set_region(self, region: str) -> 'ApiRequest':
		"""
		Adds region filter to query.
		:param region: region to filter by, see ApiConfig.regions
		:return: ApiRequest object
		"""
		for k, v in api.regions.items():
			if region in v:
				self.region = k
				return self

		self.region = None
		return self

	def set_amount(self, amount: int) -> 'ApiRequest':
		"""
		Sets the desired result count and gets the closest available.
		:param amount: desired result count, chooses closest available option, see ApiConfig.request_result_amount
		:return: ApiRequest object
		"""
		if max(api.request_result_amount.values()) >= amount >= 1:
			current_diff = -1

			for k, v in api.request_result_amount.items():
				if v >= amount:
					diff = v - amount
					if diff < current_diff or current_diff == -1:
						self.amount = k
						current_diff = diff

			if current_diff != -1:
				self.amount_wanted = amount
		else:
			self.amount_wanted = None
			self.amount = api.default_amount

		return self

	def build_query(self) -> str:
		"""
		Builds the search query.
		:return: the search query
		"""
		url = base_url + "?"

		if self.search is not None:
			url += "g={}&".format(html.escape(self.search).replace(" ", "%20"))
		if self.status is not None:
			url += "s={}&".format(self.status)
		if self.start is not None:
			url += "c={}&".format(self.start)
		if self.sort is not None:
			url += "o={}&".format(self.sort)
		if self.date is not None:
			url += "d={}&".format(self.date)
		if self.release_type is not None:
			url += "t={}&".format(self.release_type)
		if self.region is not None:
			url += "f={}&".format(self.region)

		return url + "api=v{}".format(version)

	def request(self) -> ApiResponse:
		"""
		Makes an API request to the API with the current request configuration.
		:return: the API response
		"""
		print(self.build_query())
		self.time_start = api.system_time_millis()
		return ApiResponse(
			request=self,
			data=requests.get(self.build_query()).content,
			amount_wanted=self.amount_wanted,
			custom_header=self.custom_header
		)
