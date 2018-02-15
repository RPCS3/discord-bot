import zlib


def stream_text_log(stream):
    for chunk in stream.iter_content(chunk_size=1024):
        yield chunk


def stream_gzip_decompress(stream):
    dec = zlib.decompressobj(32 + zlib.MAX_WBITS)  # offset 32 to skip the header
    for chunk in stream:
        rv = dec.decompress(chunk)
        if rv:
            yield rv
        del rv
    del dec
