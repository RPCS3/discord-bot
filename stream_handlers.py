import os
import uuid
import zipfile
import zlib


def download_file(stream, ext='.zip'):
	filename = str(uuid.uuid4()) + ext
	with open(filename, 'wb') as f:
		for chunk in stream.iter_content(chunk_size=1024 * 1024):
			if chunk:
				f.write(chunk)
	return filename


def stream_text_log(stream):
	for chunk in stream.iter_content(chunk_size=1024):
		yield chunk


def stream_gzip_decompress(stream):
	dec = zlib.decompressobj(32 + zlib.MAX_WBITS)  # offset 32 to skip the header
	for chunk in stream:
		try:
			rv = dec.decompress(chunk)
			if rv:
				yield rv
			del rv
		except zlib.error as zlr:
			pass
	del dec


def stream_zip_decompress(stream):
	filename = download_file(stream)
	with SelfDeletingFile(filename):
		try:
			with zipfile.ZipFile(filename) as z:
				for file in z.namelist():
					print(file)
					if str(file).endswith('.log'):
						with z.open(file) as f:
							for line in f:
								yield line
								del line
		except Exception as e:
			if e.args[0] == 'compression type 9 (deflate64)':
				raise Deflate64Exception


class SelfDeletingFile(object):
	def __init__(self, filename):
		self.filename = filename

	def __enter__(self):
		print('Opening self deleting temporary file: ' + self.filename)

	def __exit__(self, type, value, traceback):
		print('Removing self deleting temporary file: ' + self.filename)
		os.remove(self.filename)


class Deflate64Exception(Exception):
	pass
