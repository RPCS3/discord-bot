import re

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

    def piracy_check(self):
        for trigger in piracy_strings:
            if trigger.lower() in self.buffer.lower():
                self.trigger = trigger
                return self.ERROR_PIRACY
        return self.ERROR_SUCCESS

    def done(self):
        return self.ERROR_STOP

    def get_id(self):
        try:
            info = get_code(re.search(SERIAL_PATTERN, self.buffer).group('id'))
            if info is not None:
                self.report = info + '\n' + self.report
                return self.ERROR_SUCCESS
        except AttributeError:
            print("Could not detect serial! Aborting!")
            return self.ERROR_FAIL

    def get_libraries(self):
        try:
            self.libraries = [lib.strip().replace('.sprx', '')
                              for lib
                              in re.search(LIBRARIES_PATTERN, self.buffer).group('libraries').strip()[1:].split('-')]
            if len(self.libraries) > 0 and self.libraries[0] != "]":  # [] when empty
                self.report += 'Selected Libraries: ' + ', '.join(self.libraries) + '\n\n'
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
            'end_trigger': 'Compatibility notice:',
            'regex': re.compile('(?P<all>.*)', flags=re.DOTALL | re.MULTILINE),
            'string_format': '{all}\n\n'
        },
        {
            'end_trigger': 'Core:',
            'regex': None,
            'string_format': None,
            'function': [get_id, piracy_check]
        },
        {
            'end_trigger': 'VFS:',
            'regex': re.compile('Decoder: (?P<ppu_decoder>.*?)\n.*?'
                                'Threads: (?P<ppu_threads>.*?)\n.*?'
                                'scheduler: (?P<thread_scheduler>.*?)\n.*?'
                                'Decoder: (?P<spu_decoder>.*?)\n.*?'
                                'priority: (?P<spu_lower_thread_priority>.*?)\n.*?'
                                'SPU Threads: (?P<spu_threads>.*?)\n.*?'
                                'penalty: (?P<spu_delay_penalty>.*?)\n.*?'
                                'detection: (?P<spu_loop_detection>.*?)\n.*?'
                                'Loader: (?P<lib_loader>.*?)\n.*?'
                                'functions: (?P<hook_static_functions>.*?)\n.*',
                                flags=re.DOTALL | re.MULTILINE),
            'string_format':
                'PPU Decoder: {ppu_decoder:>21s} | Thread Scheduler: {thread_scheduler}\n'
                'SPU Decoder: {spu_decoder:>21s} | SPU Threads: {spu_threads}\n'
                'SPU Lower Thread Priority: {spu_lower_thread_priority:>7s} | Hook Static Functions: {hook_static_functions}\n'
                'SPU Loop Detection: {spu_loop_detection:>14s} | Lib Loader: {lib_loader}\n\n',
            'function': get_libraries
        },
        {
            'end_trigger': 'Video:',
            'regex': None,
            'string_format': None,
            'function': None
        },
        {
            'end_trigger': 'Audio:',
            'regex': re.compile('Renderer: (?P<renderer>.*?)\n.*?'
                                'Resolution: (?P<resolution>.*?)\n.*?'
                                'Frame limit: (?P<frame_limit>.*?)\n.*?'
                                'Write Color Buffers: (?P<write_color_buffers>.*?)\n.*?'
                                'VSync: (?P<vsync>.*?)\n.*?'
                                'Use GPU texture scaling: (?P<gpu_texture_scaling>.*?)\n.*?'
                                'Strict Rendering Mode: (?P<strict_rendering_mode>.*?)\n.*?'
                                'Resolution Scale: (?P<resolution_scale>.*?)\n.*?'
                                'Anisotropic Filter Override: (?P<af_override>.*?)\n.*?'
                                'Minimum Scalable Dimension: (?P<texture_scale_threshold>.*?)\n.*?'
                                'Adapter: (?P<gpu>((?!"").*?))\n.*?',
                                flags=re.DOTALL | re.MULTILINE),
            'string_format':
                'Renderer: {renderer:>24s} | Frame Limit: {frame_limit}\n'
                'Resolution: {resolution:>22s} | Write Color Buffers: {write_color_buffers}\n'
                'Resolution Scale: {resolution_scale:>16s} | Use GPU texture scaling: {gpu_texture_scaling}\n'
                'Resolution Scale Threshold: {texture_scale_threshold:>6s} | Anisotropic Filter Override: {af_override}\n'
                'GPU: {gpu:>29s}\n'
        },
        {
            'end_trigger': 'Log:',
            'regex': None,
            'string_format': None,
            'function': done
        }
    )

    def __init__(self):
        self.buffer = ''
        self.phase_index = 0
        self.report = ''
        self.trigger = ''
        self.libraries = []

    def feed(self, data):
        if len(self.buffer) > 16 * 1024 * 1024:
            return self.ERROR_OVERFLOW
        if self.phase[self.phase_index]['end_trigger'] in data \
                or self.phase[self.phase_index]['end_trigger'] is data.strip():
            error_code = self.process_data()
            if error_code == self.ERROR_SUCCESS:
                self.buffer = ''
                self.phase_index += 1
            else:
                return error_code
        else:
            self.buffer += '\n' + data
        return self.ERROR_SUCCESS

    def process_data(self):
        current_phase = self.phase[self.phase_index]
        if current_phase['regex'] is not None and current_phase['string_format'] is not None:
            try:
                regex_result = re.search(current_phase['regex'], self.buffer)
                if regex_result is not None:
                    group_args = regex_result.groupdict()
                    if 'strict_rendering_mode' in group_args and group_args['strict_rendering_mode'] == 'true':
                        group_args['resolution_scale'] = "Strict Mode"
                    if 'spu_threads' in group_args and group_args['spu_threads'] == '0':
                        group_args['spu_threads'] = 'auto'
                    if 'gpu' in group_args and group_args['gpu'] == '""':
                        group_args['gpu'] = ''
                    if 'af_override' in group_args:
                        if group_args['af_override'] == '0':
                            group_args['af_override'] = 'auto'
                        elif group_args['af_override'] == '1':
                            group_args['af_override'] = 'disabled'
                    self.report += current_phase['string_format'].format(**group_args)
            except AttributeError as ae:
                print(ae)
                print("Regex failed!")
                return self.ERROR_FAIL
        try:
            if current_phase['function'] is not None:
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

    def get_trigger(self):
        return self.trigger

    def get_report(self):
        return '```\n{}```'.format(self.report)
