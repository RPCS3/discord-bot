import html
from datetime import datetime

import api
from api import base_url, datetime_input_format, datetime_compatlist_query_format


class ApiRequest(object):
	def __init__(self):
		self.search = None
		self.status = None
		self.start = None
		self.sort = None
		self.date = None
		self.release_type = None
		self.region = None

	def search(self, string):
		self.search = string

	def status(self, status):
		try:
			self.status = api.statuses[status]
		except KeyError:
			self.status = None

		return self

	def startswith(self, start):
		if len(start) != 1:
			if start in ("num", "09"):
				self.start = "09"
			elif start in ("sym", "#"):
				self.start = "sym"
		else:
			self.start = start

		return self

	def sort(self, sort_type, direction):
		for k, v in api.directions:
			if direction in v:
				try:
					self.sort = api.sort_types[sort_type] + k
					return self
				except KeyError:
					self.sort = None
					return self

		return self

	def date(self, date):
		try:
			date = datetime.strptime(date, datetime_input_format)
			self.date = datetime.strftime(date, datetime_compatlist_query_format)
		except ValueError:
			self.date = None

		return self

	def release_type(self, release_type):
		for k, v in api.release_types:
			if release_type in v:
				self.release_type = k
				return self

		self.release_type = None
		return self

	def region(self, region):
		for k, v in api.regions:
			if region in v:
				self.region = k
				return self

		self.region = None
		return self

	def build_query(self):
		url = base_url + "?"
		if self.search is not None:
			url += "g={}&".format(html.escape(self.search))
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
			url += "f={}".format(self.region)
		return url
