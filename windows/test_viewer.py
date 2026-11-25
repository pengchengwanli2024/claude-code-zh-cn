import unittest

from claude_zh import load_labels, visible_labels


class VisibleLabelsTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.labels = load_labels()

    def test_onboarding_and_wrapped_trust_prompt(self):
        text, _ = next((en, zh) for en, zh in self.labels.items() if en.startswith("Quick safety check:"))
        words = text.split()
        screen = ["Welcome to Claude Code v2.1.332", " ".join(words[:12]), " ".join(words[12:]),
                  "> 1. Yes, I trust this folder", "2. No, exit", "Enter to confirm · Esc to cancel"]
        found = dict(visible_labels(screen, self.labels))
        self.assertEqual(found["Welcome to Claude Code v2.1.332"], "欢迎使用 Claude Code v2.1.332")
        self.assertEqual(found[text], self.labels[text])
        self.assertEqual(found["No, exit"], "否，退出")
        self.assertEqual(found["Yes, I trust this folder"], "是，我信任此文件夹")

    def test_no_partial_approval_or_private_values(self):
        screen = ["Yes, for this session AND forever", "ANTHROPIC_API_KEY: placeholder", "C:/private/Allow"]
        self.assertEqual(visible_labels(screen, self.labels), [])

    def test_new_visible_text_is_retained_for_offline_translation(self):
        new = "An option introduced by the next release"
        result = visible_labels(["Dark mode", new, "────────"], self.labels, include_unknown=True)
        self.assertEqual(result, [("Dark mode", "深色模式"), (new, None)])

    def test_active_theme_row(self):
        self.assertEqual(visible_labels(["> √   Auto (match terminal)"], self.labels),
                         [("Auto (match terminal)", "自动（跟随终端）")])


if __name__ == "__main__":
    unittest.main()
