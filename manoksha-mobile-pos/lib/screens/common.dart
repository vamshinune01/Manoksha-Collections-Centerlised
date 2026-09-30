import 'package:flutter/material.dart';

import '../api.dart';

void showError(BuildContext context, Object e) {
  final message = e is ApiException ? e.message : 'Something went wrong. Please try again.';
  ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message), backgroundColor: Theme.of(context).colorScheme.error));
}

void showInfo(BuildContext context, String message) => ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));

class Busy extends StatelessWidget {
  const Busy({super.key});

  @override
  Widget build(BuildContext context) => const Center(
    child: Padding(padding: EdgeInsets.all(24), child: CircularProgressIndicator()),
  );
}
