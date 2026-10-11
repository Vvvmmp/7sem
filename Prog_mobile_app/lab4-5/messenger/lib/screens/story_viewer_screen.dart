import 'package:flutter/material.dart';

import '../data/models/story.dart';

class StoryViewerScreen extends StatefulWidget {
  const StoryViewerScreen({super.key, required this.stories, required this.initialIndex});

  final List<StoryItem> stories;
  final int initialIndex;

  @override
  State<StoryViewerScreen> createState() => _StoryViewerScreenState();
}

class _StoryViewerScreenState extends State<StoryViewerScreen> {
  late final PageController _controller = PageController(initialPage: widget.initialIndex);
  late int _current = widget.initialIndex;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final story = widget.stories[_current];
    return Scaffold(
      backgroundColor: Colors.black,
      body: Stack(
        children: [
          PageView.builder(
            controller: _controller,
            itemCount: widget.stories.length,
            onPageChanged: (index) => setState(() => _current = index),
            itemBuilder: (_, index) => Image.asset(
              widget.stories[index].imageAsset,
              fit: BoxFit.cover,
              width: double.infinity,
              height: double.infinity,
            ),
          ),
          SafeArea(
            child: Padding(
              padding: const EdgeInsets.all(12),
              child: Row(
                children: [
                  if (story.avatarAsset != null)
                    CircleAvatar(radius: 20, backgroundImage: AssetImage(story.avatarAsset!)),
                  const SizedBox(width: 10),
                  Text(
                    story.name,
                    style: const TextStyle(color: Colors.white, fontSize: 17, fontWeight: FontWeight.w700),
                  ),
                  const SizedBox(width: 10),
                  Text(
                    '${_current + 1}/${widget.stories.length}',
                    style: const TextStyle(color: Colors.white70, fontSize: 14),
                  ),
                  const Spacer(),
                  IconButton(
                    icon: const Icon(Icons.close, color: Colors.white),
                    onPressed: () => Navigator.pop(context),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
