from datetime import datetime.strptime, date.strftime

class ApiResult(object):
	def __init__(self, id, data):
		self.id = id
		self.title = data["title"]
		self.status = data["status"]
		self.date = strptime("%Y-%m-%d", data["date"])
		self.thread = data["thread"]
		self.commit = data["commit"]
		self.pr = data["pr"]
	
	def to_chat_string(self):
		return "ID:{:9s} Title:{:40s} PR:{:4s} Status:{:8s} Updated:{:10s}".format(
			self.id,
			self.title,
			self.pr,
			self.status,
			self.date,
			strftime("%Y-%m-%d", self.date)
		)
