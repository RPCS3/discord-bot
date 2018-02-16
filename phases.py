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
            if trigger in self.buffer:
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
            if len(self.libraries) > 0 and self.libraries[0] != "]": # [] when empty
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
                'PPU Decoder: {ppu_decoder:>21s} | PPU Threads: {ppu_threads}\n'
                'SPU Decoder: {spu_decoder:>21s} | SPU Threads: {spu_threads}\n'
                'SPU Lower Thread Priority: {spu_lower_thread_priority:>7s} | SPU Delay Penalty: {spu_delay_penalty}\n'
                'SPU Loop Detection: {spu_loop_detection:>14s} | Hook Static Functions: {hook_static_functions}\n'
                'Thread Scheduler: {thread_scheduler:>16s} | Lib Loader: {lib_loader}\n\n',
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
                                'limit: (?P<frame_limit>.*?)\n.*?'
                                'Color Buffers: (?P<write_color_buffers>.*?)\n.*?'
                                'VSync: (?P<vsync>.*?)\n.*?'
                                'Rendering Mode: (?P<strict_rendering_mode>.*?)\n.*?',
                                flags=re.DOTALL | re.MULTILINE),
            'string_format':
                'Renderer: {renderer:>24s} | Resolution: {resolution}\n'
                'Frame Limit: {frame_limit:>21s} | Write Color Buffers: {write_color_buffers}\n'
                'VSync: {vsync:>27s} | Strict Rendering Mode: {strict_rendering_mode}\n'
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
                self.report += current_phase['string_format'].format(
                    **re.search(current_phase['regex'], self.buffer).groupdict()
                )
            except AttributeError as ae:
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
