import 'package:flutter/material.dart';
import 'package:print_bluetooth_thermal/print_bluetooth_thermal.dart';

import '../api.dart';
import '../app_state.dart';
import '../printing.dart';
import 'common.dart';

class SettingsScreen extends StatefulWidget {
  const SettingsScreen({super.key});

  @override
  State<SettingsScreen> createState() => _SettingsScreenState();
}

class _SettingsScreenState extends State<SettingsScreen> {
  List<BluetoothInfo>? _printers;

  Future<void> _findPrinters() async {
    if (!await ReceiptPrinter.ensurePermissions()) {
      if (mounted) showInfo(context, 'Allow Bluetooth access to use a printer.');
      return;
    }
    final list = await ReceiptPrinter.pairedPrinters();
    if (mounted) setState(() => _printers = list);
    if (list.isEmpty && mounted) showInfo(context, 'No paired devices. Pair the printer in Android Bluetooth settings first.');
  }

  Future<void> _setPin() async {
    final password = TextEditingController();
    final pin = TextEditingController();
    final ok = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Approval PIN'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Text('Used when you approve a discount on someone else\'s sale. 4–6 digits.'),
            TextField(
              controller: password,
              obscureText: true,
              decoration: const InputDecoration(labelText: 'Your password'),
            ),
            TextField(
              controller: pin,
              obscureText: true,
              keyboardType: TextInputType.number,
              maxLength: 6,
              decoration: const InputDecoration(labelText: 'New PIN'),
            ),
          ],
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Save')),
        ],
      ),
    );
    if (ok != true) return;
    try {
      await Api.instance.put('auth/me/approval-pin', {'currentPassword': password.text, 'pin': pin.text});
      if (mounted) showInfo(context, 'Approval PIN saved.');
    } catch (e) {
      if (mounted) showError(context, e);
    }
  }

  @override
  Widget build(BuildContext context) {
    final app = AppState.instance;
    return Scaffold(
      appBar: AppBar(title: const Text('Settings')),
      body: ListenableBuilder(
        listenable: app,
        builder: (context, _) => ListView(
          children: [
            ListTile(title: Text(app.displayName), subtitle: Text(app.branch?.name ?? 'No branch')),
            const Divider(),
            ListTile(
              leading: const Icon(Icons.print),
              title: Text(app.printerName ?? 'No printer'),
              subtitle: const Text('Bluetooth thermal printer'),
              trailing: TextButton(onPressed: _findPrinters, child: const Text('Choose')),
            ),
            if (_printers != null)
              for (final p in _printers!)
                ListTile(
                  contentPadding: const EdgeInsets.only(left: 56, right: 16),
                  title: Text(p.name),
                  subtitle: Text(p.macAdress),
                  selected: p.macAdress == app.printerMac,
                  onTap: () => app.savePrinter(p.macAdress, p.name, app.paper80mm),
                ),
            SwitchListTile(
              title: const Text('80 mm paper'),
              subtitle: const Text('Off = 58 mm'),
              value: app.paper80mm,
              onChanged: (v) => app.savePrinter(app.printerMac, app.printerName, v),
            ),
            ListTile(
              enabled: app.printerMac != null,
              leading: const Icon(Icons.receipt_long),
              title: const Text('Print test receipt'),
              onTap: () async {
                final err = await ReceiptPrinter.printTest();
                if (context.mounted) showInfo(context, err ?? 'Test receipt sent.');
              },
            ),
            if (app.printerMac != null)
              ListTile(leading: const Icon(Icons.link_off), title: const Text('Forget printer'), onTap: () => app.savePrinter(null, null, app.paper80mm)),
            const Divider(),
            if (app.isOwner || app.branches.any((b) => b.canApprove))
              ListTile(leading: const Icon(Icons.pin), title: const Text('Set approval PIN'), onTap: _setPin),
            ListTile(
              leading: const Icon(Icons.logout),
              title: const Text('Sign out'),
              onTap: () async {
                Navigator.of(context).popUntil((r) => r.isFirst);
                await app.signOut();
              },
            ),
          ],
        ),
      ),
    );
  }
}
