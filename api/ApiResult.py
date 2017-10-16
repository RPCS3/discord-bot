"""
ApiResult class
"""

from datetime import datetime
from typing import Dict

from api import datetime_input_format, datetime_output_format, trim_string


class ApiResult(object):
	"""
	API Result object
	"""

	def __init__(self, game_id: str, data: Dict) -> None:
		self.game_id = game_id
		self.title = data["title"]
		self.status = data["status"]
		self.date = datetime.strptime(data["date"], datetime_input_format)
		self.thread = data["thread"]
		self.commit = data["commit"]
		self.pr = data["pr"]

	def to_string(self) -> str:
		"""
		Makes a string representation of the object.
		:return: string representation of the object
		"""
		return "ID:{:9s} Title:{:40s} PR:{:4s} Status:{:8s} Updated:{:10s}".format(
			self.game_id,
			trim_string(self.title, 40),
			self.pr,
			self.status,
			self.date,
			datetime.strftime(self.date, datetime_output_format)
		)
