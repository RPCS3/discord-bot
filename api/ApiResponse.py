import json

import ApiResult

class ApiResponse(object):
	def __init__(self, data):
		parsed_data = json.loads(data)
		self.code = parsed_data["return_code"]
		self.load_results(parsed_data["results"])
	
	def load_results(self, data):
		self.results = array()
		for id, result_data in data:
			self.results.append(ApiResult(id, result_data))
		
