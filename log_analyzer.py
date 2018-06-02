import re

import itertools
from collections import deque
from api import sanitize_string
from api.result import ApiResult
from bot_config import piracy_strings
from bot_utils import get_code

SERIAL_PATTERN = re.compile('Serial: (?P<id>[A-z]{4}\\d{5})')
LIBRARIES_PATTERN = re.compile('Load libraries:(?P<libraries>.*)', re.DOTALL | re.MULTILINE)


class LogAnalyzer(object):
    ERROR_SUCCESS = 0
    ERROR_PIRACY = 1
    ERROR_STOP = 2
    ERROR_OVERFLOW = -1
    ERROR_FAIL = -2
    ERROR_DEFLATE_64 = -3

    def clear_results(self):
        if self is None or self.parsed_data is None:
            return self.ERROR_SUCCESS
        if self.build_and_specs is None:
            self.build_and_specs = self.parsed_data["build_and_specs"]
        self.trigger = ''
        self.buffer = ''
        self.buffer_lines.clear()
        self.libraries = []
        self.parsed_data = { "build_and_specs": self.build_and_specs }
        return self.ERROR_SUCCESS

    def piracy_check(self):
        for trigger in piracy_strings:
            if trigger.lower() in self.buffer.lower():
                self.trigger = trigger
                return self.ERROR_PIRACY
        return self.ERROR_SUCCESS

    def done(self):
        return self.ERROR_STOP

    def done_and_reset(self):
        self.phase_index = 0
        return self.ERROR_STOP

    def get_id(self):
        try:
            self.product_info = get_code(re.search(SERIAL_PATTERN, self.buffer).group('id'))
            return self.ERROR_SUCCESS
        except AttributeError:
            self.product_info = ApiResult(None, dict({"status": "Unknown"}))
            return self.ERROR_SUCCESS
    
    def parse_rsx(self):
        if len(self.buffer_lines) < 4:
            return False
        if  all(line.startswith('·!') for line in itertools.islice(self.buffer_lines, 4)) and \
            all('RSX: ' in line for line in itertools.islice(self.buffer_lines, 4)):
            return True
        return False

    def get_libraries(self):
        try:
            self.libraries = [lib.strip().replace('.sprx', '')
                              for lib
                              in re.search(LIBRARIES_PATTERN, self.buffer).group('libraries').strip()[1:].split('-')]
        except KeyError as ke:
            print(ke)
            pass
        return self.ERROR_SUCCESS
    
    """
    End Trigger
    Regex
    Message To Print
    Special Return
    """
    phase = (
        {
            'end_trigger': '·',
            'regex': re.compile('(?P<build_and_specs>.*)', flags=re.DOTALL | re.MULTILINE),
            'function': clear_results
        },
        {
            'end_trigger': 'Core:',
            'regex': re.compile('Path: (?:(?P<win_path>\\w:/)|(?P<lin_path>/)).*?\n'
                                '(?:.* custom config: (?P<custom_config>.*?)\n.*?)?',
                                flags=re.DOTALL | re.MULTILINE),
            'function': [get_id, piracy_check]
        },
        {
            'end_trigger': 'VFS:',
            'regex': re.compile('Decoder: (?P<ppu_decoder>.*?)\n.*?'
                                'Threads: (?P<ppu_threads>.*?)\n.*?'
                                '(?:scheduler: (?P<thread_scheduler>.*?)\n.*?)?'
                                'Decoder: (?P<spu_decoder>.*?)\n.*?'
                                '(?:secondary cores: (?P<spu_secondary_cores>.*?)\n.*?)?'
                                'priority: (?P<spu_lower_thread_priority>.*?)\n.*?'
                                'SPU Threads: (?P<spu_threads>.*?)\n.*?'
                                'penalty: (?P<spu_delay_penalty>.*?)\n.*?'
                                'detection: (?P<spu_loop_detection>.*?)\n.*?'
                                '[Ll]oader: (?P<lib_loader>.*?)\n.*?'
                                'functions: (?P<hook_static_functions>.*?)\n.*',
                                flags=re.DOTALL | re.MULTILINE),
            'function': get_libraries
        },
        {
            'end_trigger': 'Video:',
            'regex': None,
            'function': None
        },
        {
            'end_trigger': 'Audio:',
            'regex': re.compile('Renderer: (?P<renderer>.*?)\n.*?'
                                'Resolution: (?P<resolution>.*?)\n.*?'
                                'Aspect ratio: (?P<aspect_ratio>.*?)\n.*?'
                                'Frame limit: (?P<frame_limit>.*?)\n.*?'
                                'Write Color Buffers: (?P<write_color_buffers>.*?)\n.*?'
                                'VSync: (?P<vsync>.*?)\n.*?'
                                'Use GPU texture scaling: (?P<gpu_texture_scaling>.*?)\n.*?'
                                'Strict Rendering Mode: (?P<strict_rendering_mode>.*?)\n.*?'
                                'Disable Vertex Cache: (?P<vertex_cache>.*?)\n.*?'
                                'Blit: (?P<cpu_blit>.*?)\n.*?'
                                'Resolution Scale: (?P<resolution_scale>.*?)\n.*?'
                                'Anisotropic Filter Override: (?P<af_override>.*?)\n.*?'
                                'Minimum Scalable Dimension: (?P<texture_scale_threshold>.*?)\n.*?'
                                '(?:D3D12|DirectX 12):\\s*\n\\s*Adapter: (?P<d3d_gpu>.*?)\n.*?'
                                'Vulkan:\\s*\n\\s*Adapter: (?P<vulkan_gpu>.*?)\n.*?',
                                flags=re.DOTALL | re.MULTILINE)
        },
        {
            'end_trigger': 'Log:',
            'regex': None,
            'function': done
        },
        {
            'end_trigger': 'Objects cleared...',
            'regex': re.compile('(?:'
                                'RSX:(?:\\d|\\.|\\s|\\w|-)* (?P<driver_version>(?:\\d+\\.)*\\d+)\n[^\n]*?'
                                'RSX: [^\n]+\n[^\n]*?'
                                'RSX: (?P<driver_manuf>.*?)\n[^\n]*?'
                                'RSX: Supported texel buffer size reported: (?P<texel_buffer_size>\\d*?) bytes'
                                ')|(?:'
                                'GL RENDERER: (?P<driver_manuf_new>.*?)\n[^\n]*?'
                                'GL VERSION:(?:\\d|\\.|\\s|\\w|-)* (?P<driver_version_new>(?:\\d+\\.)*\\d+)\n[^\n]*?'
                                'RSX: [^\n]+\n[^\n]*?'
                                'RSX: Supported texel buffer size reported: (?P<texel_buffer_size_new>\\d*?) bytes'
                                ')\n.*?',
                                flags=re.DOTALL | re.MULTILINE),
            'on_buffer_flush': parse_rsx,
            'function': done_and_reset
        }
    )

    def __init__(self):
        self.buffer = ''
        self.buffer_lines = deque([])
        self.total_data_len = 0
        self.phase_index = 0
        self.build_and_specs = None
        self.trigger = ''
        self.libraries = []
        self.parsed_data = {}

    def feed(self, data):
        self.total_data_len += len(data)
        if self.total_data_len > 32 * 1024 * 1024:
            return self.ERROR_OVERFLOW
        if self.phase[self.phase_index]['end_trigger'] in data \
                or self.phase[self.phase_index]['end_trigger'] is data.strip():
            error_code = self.process_data()
            self.buffer = ''
            self.buffer_lines.clear()
            if error_code == self.ERROR_SUCCESS or error_code == self.ERROR_STOP:
                self.phase_index += 1
            if error_code != self.ERROR_SUCCESS:
                self.sanitize()
            return error_code
        else:
            if len(self.buffer_lines) > 256:
                error_code = self.process_data(True)
                if error_code != self.ERROR_SUCCESS:
                    self.sanitize()
                    return error_code
                self.buffer_lines.popleft()
            self.buffer_lines.append(data)
        return self.ERROR_SUCCESS

    def process_data(self, on_buffer_flush = False):
        current_phase = self.phase[self.phase_index]
        # if buffer was flushed and check function found relevant data, run regex
        if on_buffer_flush:
            try:
                if current_phase['on_buffer_flush'] is not None:
                    if not current_phase['on_buffer_flush'](self):
                        return self.ERROR_SUCCESS
            except KeyError:
                pass
        self.buffer = '\n'.join(self.buffer_lines)
        if current_phase['regex'] is not None:
            try:
                regex_result = re.search(current_phase['regex'], self.buffer.strip() + '\n')
                if regex_result is not None:
                    group_args = regex_result.groupdict()
                    self.parsed_data.update(group_args)
            except AttributeError as ae:
                print(ae)
                print("Regex failed!")
                return self.ERROR_FAIL
        try:
            # run funcitons only on end_trigger
            if current_phase['function'] is not None and not on_buffer_flush:
                if isinstance(current_phase['function'], list):
                    for func in current_phase['function']:
                        error_code = func(self)
                        if error_code != self.ERROR_SUCCESS:
                            return error_code
                    return self.ERROR_SUCCESS
                else:
                    return current_phase['function'](self)
        except KeyError:
            pass
        return self.ERROR_SUCCESS

    def sanitize(self):
        result = {}
        for k, v in self.parsed_data.items():
            r = sanitize_string(v)
            if r is not None:
                if r == "true":
                    r = "[x]"
                elif r == "false":
                    r = "[ ]"
            result[k] = r
        self.parsed_data = result
        libs = []
        for l in self.libraries:
            libs.append(sanitize_string(l))
        self.libraries = libs

    def get_trigger(self):
        return self.trigger

    def process_final_data(self):
        group_args = self.parsed_data
        if 'strict_rendering_mode' in group_args and group_args['strict_rendering_mode'] == 'true':
            group_args['resolution_scale'] = "Strict Mode"
        if 'spu_threads' in group_args and group_args['spu_threads'] == '0':
            group_args['spu_threads'] = 'Auto'
        if 'spu_secondary_cores' in group_args and group_args['spu_secondary_cores'] is not None:
            group_args['thread_scheduler'] = group_args['spu_secondary_cores']
        if 'vulkan_gpu' in group_args and group_args['vulkan_gpu'] != '""':
            group_args['gpu_info'] = group_args['vulkan_gpu']
        elif 'd3d_gpu' in group_args and group_args['d3d_gpu'] != '""':
            group_args['gpu_info'] = group_args['d3d_gpu']
        elif 'driver_manuf_new' in group_args and group_args['driver_manuf_new'] is not None:
            group_args['gpu_info'] = group_args['driver_manuf_new']
        elif 'driver_manuf' in group_args and group_args['driver_manuf'] is not None:
            group_args['gpu_info'] = group_args['driver_manuf']
        else:
            group_args['gpu_info'] = 'Unknown'
        if 'driver_version_new' in group_args and group_args["driver_version_new"] is not None:
            group_args["gpu_info"] = group_args["gpu_info"] + " (" + group_args["driver_version_new"] + ")"
        elif 'driver_version' in group_args and group_args["driver_version"] is not None:
            group_args["gpu_info"] = group_args["gpu_info"] + " (" + group_args["driver_version"] + ")"
        if 'af_override' in group_args:
            if group_args['af_override'] == '0':
                group_args['af_override'] = 'Auto'
            elif group_args['af_override'] == '1':
                group_args['af_override'] = 'Disabled'

    def get_text_report(self):
        self.process_final_data()
        additional_info = {
            'product_info': self.product_info.to_string(),
            'libs': ', '.join(self.libraries) if len(self.libraries) > 0 and self.libraries[0] != "]" else "None",
            'os': 'Windows' if 'win_path' in self.parsed_data and self.parsed_data['win_path'] is not None else 'Linux',
            'config': '\nUsing custom config!\n' if 'custom_config' in self.parsed_data and self.parsed_data['custom_config'] is not None else ''
        }
        additional_info.update(self.parsed_data)
        return (
            '```'
            '{product_info}\n'
            '\n'
            '{build_and_specs}'
            'GPU: {gpu_info}\n'
            'OS: {os}\n'
            '{config}\n'
            'PPU Decoder: {ppu_decoder:>21s} | Thread Scheduler: {thread_scheduler}\n'
            'SPU Decoder: {spu_decoder:>21s} | SPU Threads: {spu_threads}\n'
            'SPU Lower Thread Priority: {spu_lower_thread_priority:>7s} | Hook Static Functions: {hook_static_functions}\n'
            'SPU Loop Detection: {spu_loop_detection:>14s} | Lib Loader: {lib_loader}\n'
            '\n'
            'Selected Libraries: {libs}\n'
            '\n'
            'Renderer: {renderer:>24s} | Frame Limit: {frame_limit}\n'
            'Resolution: {resolution:>22s} | Write Color Buffers: {write_color_buffers}\n'
            'Resolution Scale: {resolution_scale:>16s} | Use GPU texture scaling: {gpu_texture_scaling}\n'
            'Resolution Scale Threshold: {texture_scale_threshold:>6s} | Anisotropic Filter Override: {af_override}\n'
            'VSync: {vsync:>27s} | Disable Vertex Cache: {vertex_cache}\n'
            '```'
        ).format(**additional_info)

    def get_embed_report(self):
        self.process_final_data()
        lib_loader = self.parsed_data['lib_loader']
        if lib_loader is not None:
            lib_loader = lib_loader.lower()
        lib_loader_auto = 'auto' in lib_loader
        lib_loader_manual = 'manual' in lib_loader
        if lib_loader_auto and lib_loader_manual:
            self.parsed_data['lib_loader'] = "Auto & manual select"
        elif lib_loader_auto:
            self.parsed_data['lib_loader'] = "Auto"
        elif lib_loader_manual:
            self.parsed_data['lib_loader'] = "Manual selection"
        custom_config = 'custom_config' in self.parsed_data and self.parsed_data['custom_config'] is not None
        self.parsed_data['os_path'] = 'Windows' if 'win_path' in self.parsed_data and self.parsed_data['win_path'] is not None else 'Linux'
        result = self.product_info.to_embed(False).add_field(
            name='Build Info',
            value=(
                '{build_and_specs}'
                'GPU: {gpu_info}'
            ).format(**self.parsed_data),
            inline=False
        ).add_field(
            name='CPU Settings' if not custom_config else 'Per-game CPU Settings',
            value=(
                '`PPU Decoder: {ppu_decoder:>21s}`\n'
                '`SPU Decoder: {spu_decoder:>21s}`\n'
                '`SPU Lower Thread Priority: {spu_lower_thread_priority:>7s}`\n'
                '`SPU Loop Detection: {spu_loop_detection:>14s}`\n'
                '`Thread Scheduler: {thread_scheduler:>16s}`\n'
                '`Detected OS: {os_path:>21s}`\n'
                '`SPU Threads: {spu_threads:>21s}`\n'
                '`Force CPU Blit: {cpu_blit:>18s}`\n'
                '`Hook Static Functions: {hook_static_functions:>11s}`\n'
                '`Lib Loader: {lib_loader:>22s}`\n'
            ).format(**self.parsed_data),
            inline=True
        ).add_field(
            name='GPU Settings' if not custom_config else 'Per-game GPU Settings',
            value=(
                '`Renderer: {renderer:>24s}`\n'
                '`Aspect ratio: {aspect_ratio:>20s}`\n'
                '`Resolution: {resolution:>22s}`\n'
                '`Resolution Scale: {resolution_scale:>16s}`\n'
                '`Resolution Scale Threshold: {texture_scale_threshold:>6s}`\n'
                '`Write Color Buffers: {write_color_buffers:>13s}`\n'
                '`Use GPU texture scaling: {gpu_texture_scaling:>9s}`\n'
                '`Anisotropic Filter: {af_override:>14s}`\n'
                '`Frame Limit: {frame_limit:>21s}`\n'
#                '`VSync: {vsync:>27s}`\n'
                '`Disable Vertex Cache: {vertex_cache:>12s}`\n'
            ).format(**self.parsed_data),
            inline=True
        )
        if 'manual' in self.parsed_data['lib_loader'].lower():
            result = result.add_field(
                name="Selected Libraries",
                value=', '.join(self.libraries) if len(self.libraries) > 0 and self.libraries[0] != "]" else "None",
                inline=False
            )
        return result
