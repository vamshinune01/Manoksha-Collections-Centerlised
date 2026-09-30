import 'package:flutter/material.dart';

import '../app_state.dart';
import 'attendance_screen.dart';
import 'lookup_screen.dart';
import 'sale_screen.dart';
import 'sales_today_screen.dart';
import 'settings_screen.dart';

class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final app = AppState.instance;
    if (app.branches.isEmpty) {
      return Scaffold(
        appBar: AppBar(title: const Text('Manoksha POS')),
        body: Center(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Text('Your account cannot sell at any branch yet. Ask the Owner to assign you a role at your branch.', textAlign: TextAlign.center),
                const SizedBox(height: 16),
                OutlinedButton(onPressed: app.signOut, child: const Text('Sign out')),
              ],
            ),
          ),
        ),
      );
    }
    if (app.branch == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('Choose your branch')),
        body: ListView(
          children: [for (final b in app.branches) ListTile(title: Text(b.name), subtitle: Text(b.code), onTap: () => app.selectBranch(b))],
        ),
      );
    }
    void open(Widget page) => Navigator.of(context).push(MaterialPageRoute(builder: (_) => page));
    final tiles = <(IconData, String, Widget)>[
      (Icons.point_of_sale, 'New sale', const SaleScreen()),
      (Icons.receipt_long, "Today's sales", const SalesTodayScreen()),
      (Icons.qr_code_scanner, 'Item lookup', const LookupScreen()),
      (Icons.access_time, 'Attendance', const AttendanceScreen()),
      (Icons.settings, 'Settings', const SettingsScreen()),
    ];
    return Scaffold(
      appBar: AppBar(
        title: Text(app.branch!.name),
        actions: [
          if (app.branches.length > 1) IconButton(tooltip: 'Change branch', icon: const Icon(Icons.store), onPressed: () => _chooseBranch(context)),
          IconButton(tooltip: 'Sign out', icon: const Icon(Icons.logout), onPressed: app.signOut),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text('Hello, ${app.displayName}', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 16),
          GridView.count(
            crossAxisCount: MediaQuery.sizeOf(context).width > 700 ? 3 : 2,
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            mainAxisSpacing: 12,
            crossAxisSpacing: 12,
            children: [
              for (final (icon, label, page) in tiles)
                Card(
                  child: InkWell(
                    onTap: () => open(page),
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(icon, size: 40, color: Theme.of(context).colorScheme.primary),
                        const SizedBox(height: 8),
                        Text(label, style: const TextStyle(fontSize: 16)),
                      ],
                    ),
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }

  void _chooseBranch(BuildContext context) {
    final app = AppState.instance;
    showModalBottomSheet<void>(
      context: context,
      builder: (_) => ListView(
        shrinkWrap: true,
        children: [
          for (final b in app.branches)
            ListTile(
              title: Text(b.name),
              onTap: () {
                app.selectBranch(b);
                Navigator.of(context).pop();
              },
            ),
        ],
      ),
    );
  }
}
